using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

using CvPoint = OpenCvSharp.Point;
using CvPoint2f = OpenCvSharp.Point2f;

namespace PaddleOCR_CSharp
{
    // Mat对象池，用于重用Mat对象以减少GC压力
    public class MatPool
    {
        private readonly ConcurrentQueue<Mat> _pool = new ConcurrentQueue<Mat>();
        private readonly int _maxPoolSize;

        public MatPool(int maxPoolSize = 50)
        {
            _maxPoolSize = maxPoolSize;
        }

        public Mat Rent(int rows, int cols, MatType type)
        {
            // 尝试从池中找到合适的Mat对象
            var tempQueue = new Queue<Mat>();
            Mat? suitableMat = null;
            
            try
            {
                // 遍历池中的Mat对象，寻找合适的
                while (_pool.TryDequeue(out Mat? mat))
                {
                    if (mat.Type() == type && mat.Rows >= rows && mat.Cols >= cols)
                    {
                        // 找到合适的Mat：类型相同且容量足够
                        suitableMat = mat;
                        break;
                    }
                    else
                    {
                        // 暂存不合适的Mat，稍后放回池中
                        tempQueue.Enqueue(mat);
                    }
                }
                
                // 将暂存的Mat放回池中
                while (tempQueue.Count > 0)
                {
                    _pool.Enqueue(tempQueue.Dequeue());
                }
                
                if (suitableMat != null)
                {
                    // 如果尺寸完全匹配，直接返回
                    if (suitableMat.Rows == rows && suitableMat.Cols == cols)
                    {
                        return suitableMat;
                    }
                    else
                    {
                        // 创建指定尺寸的Mat视图，复用底层内存
                        // 注意：这里创建的是原Mat的一个ROI视图
                        var roi = new OpenCvSharp.Rect(0, 0, cols, rows);
                        var resizedView = new Mat(suitableMat, roi);
                        
                        // 将原Mat标记为已使用（通过创建视图的方式）
                        // 原Mat会在视图释放时自动管理
                        return resizedView;
                    }
                }
            }
            finally
            {
                // 确保所有暂存的Mat都放回池中
                while (tempQueue.Count > 0)
                {
                    var mat = tempQueue.Dequeue();
                    if (!mat.IsDisposed)
                    {
                        _pool.Enqueue(mat);
                    }
                    else
                    {
                        mat.Dispose();
                    }
                }
            }
            
            // 没有找到合适的Mat，创建新的
            return new Mat(rows, cols, type);
        }

        public void Return(Mat mat)
        {
            if (mat != null && !mat.IsDisposed && _pool.Count < _maxPoolSize)
            {
                _pool.Enqueue(mat);
            }
            else
            {
                mat?.Dispose();
            }
        }

        public void Clear()
        {
            while (_pool.TryDequeue(out Mat? mat))
            {
                mat.Dispose();
            }
        }
    }

    // BoundingBox对象池，减少小对象分配
    public class BoundingBoxPool
    {
        private readonly ConcurrentQueue<BoundingBox> _pool = new ConcurrentQueue<BoundingBox>();
        private readonly int _maxPoolSize;

        public BoundingBoxPool(int maxPoolSize = 50)
        {
            _maxPoolSize = maxPoolSize;
        }

        public BoundingBox Rent()
        {
            if (_pool.TryDequeue(out BoundingBox? box))
            {
                return box;
            }
            return new BoundingBox();
        }

        public void Return(BoundingBox box)
        {
            if (box != null && _pool.Count < _maxPoolSize)
            {
                // 重置对象状态
                box.XMin = 0;
                box.YMin = 0;
                box.XMax = 0;
                box.YMax = 0;
                _pool.Enqueue(box);
            }
        }

        public void Clear()
        {
            while (_pool.TryDequeue(out _))
            {
                // BoundingBox不需要特殊清理
            }
        }
    }

    // OCRResult对象池
    public class OCRResultPool
    {
        private readonly ConcurrentQueue<OCRResult> _pool = new ConcurrentQueue<OCRResult>();
        private readonly int _maxPoolSize;

        public OCRResultPool(int maxPoolSize = 50)
        {
            _maxPoolSize = maxPoolSize;
        }

        public OCRResult Rent()
        {
            if (_pool.TryDequeue(out OCRResult? result))
            {
                return result;
            }
            return new OCRResult { Text = "", Box = new BoundingBox() };
        }

        public void Return(OCRResult result)
        {
            if (result != null && _pool.Count < _maxPoolSize)
            {
                // 重置对象状态
                result.Text = "";
                result.Confidence = 0f;
                // 注意：这里不重置Box，因为它可能被其他地方引用
                _pool.Enqueue(result);
            }
        }

        public void Clear()
        {
            while (_pool.TryDequeue(out _))
            {
                // OCRResult不需要特殊清理
            }
        }
    }

    public class OCRResult
    {
        public required string Text { get; set; }
        public float Confidence { get; set; }
        public required BoundingBox Box { get; set; }
    }

    public class BoundingBox
    {
        public int XMin { get; set; }
        public int YMin { get; set; }
        public int XMax { get; set; }
        public int YMax { get; set; }

        public int Width => XMax - XMin;
        public int Height => YMax - YMin;
    }

    public class OCRManager : IDisposable
    {
        private InferenceSession detectionSession;
        private InferenceSession recognitionSession;
        private bool disposed = false;

        private List<string> recLabels;
        private List<string> layoutLabels;
        private List<string> tableLabels;

        private string detectionModelPath;
        private string recognitionModelPath;
        

        // 对象池和数组池
        private readonly MatPool _matPool = new MatPool(100);
        private readonly BoundingBoxPool _boundingBoxPool = new BoundingBoxPool(100);
        private readonly OCRResultPool _ocrResultPool = new OCRResultPool(100);
        private static readonly ArrayPool<float> _floatArrayPool = ArrayPool<float>.Shared;
        private static readonly Dictionary<string, List<string>> _labelCache = new Dictionary<string, List<string>>();
        private static readonly object _cacheLock = new object();

        //错误信息
        public string LastErrorMessage { get; private set; }

        //模型参数
        private string limit_type_ = "max";
        private int limit_side_len_ = 960;
        private bool use_tensorrt_ = false;

        private List<float> det_mean_ = new List<float> { 0.485f, 0.456f, 0.406f };
        private List<float> det_scale_ = new List<float> { 1f / 0.229f, 1f / 0.224f, 1f / 0.225f };
        private bool det_is_scale_ = true;

        private int rec_img_h_ = 48;
        private int rec_img_w_ = 320;
        private List<float> rec_mean_ = new List<float> { 0.5f, 0.5f, 0.5f };
        private List<float> rec_scale_ = new List<float> { 1 / 0.5f, 1 / 0.5f, 1 / 0.5f };
        private bool rec_is_scale_ = true;

        private float det_db_thresh_ = 0.3f;
        private float det_db_box_thresh_ = 0.6f;
        private float det_db_unclip_ratio_ = 1.5f;  //default 1.5f ,配合原版 UnClip
        private bool use_dilation_ = false;

        
        public OCRManager(
            string detectionModelPath,
            string recognitionModelPath,
            string recLabelPath,
            string layoutLabelPath,
            string tableLabelPath)
        {
            try
            {
                LastErrorMessage = string.Empty;
                
                this.detectionModelPath = detectionModelPath;
                this.recognitionModelPath = recognitionModelPath;

                recLabels = LoadLabels(recLabelPath);
                recLabels.Insert(0, "#");
                recLabels.Add(" ");
                layoutLabels = LoadLabels(layoutLabelPath);
                tableLabels = LoadLabels(tableLabelPath);

                var detectionOptions = new SessionOptions();
                var recognitionOptions = new SessionOptions();

                // 优化ONNX Runtime会话配置
                ConfigureSessionOptions(detectionOptions);
                ConfigureSessionOptions(recognitionOptions);

                detectionSession = new InferenceSession(this.detectionModelPath, detectionOptions);

                recognitionSession = new InferenceSession(this.recognitionModelPath, recognitionOptions);
                
            }
            catch (Exception ex)
            {
                LastErrorMessage = $"模型加载失败: {ex.Message}\n堆栈跟踪: {ex.StackTrace}";
                throw new Exception(LastErrorMessage, ex);
            }
        }

        private List<string> LoadLabels(string filePath)
        {
            if (!File.Exists(filePath))
            {
                LastErrorMessage = $"标签文件未找到: {filePath}";
                throw new FileNotFoundException(LastErrorMessage);
            }

            // 使用缓存避免重复读取文件
            lock (_cacheLock)
            {
                if (_labelCache.TryGetValue(filePath, out List<string>? cachedLabels))
                {
                    return new List<string>(cachedLabels); // 返回副本
                }

                var labels = File.ReadAllLines(filePath).ToList();
                _labelCache[filePath] = labels;
                return new List<string>(labels);
            }
        }

        private void ConfigureSessionOptions(SessionOptions options)
        {
            // 启用CPU执行提供程序
            options.AppendExecutionProvider_CPU();
            
            // 启用所有图优化
            options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            
            // 启用内存模式优化
            options.EnableMemoryPattern = true;
            options.EnableCpuMemArena = true;
            
            // 设置线程数为CPU核心数/2
            // 启用并行执行
            options.InterOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
            
            // 设置执行模式为并行
            options.ExecutionMode = ExecutionMode.ORT_PARALLEL;
        }

        public static Mat BitmapToMat(Bitmap bitmap)
        {
            // 检查像素格式，目前已经是24bppRgb直接使用
            Bitmap workingBitmap = bitmap;
            
            var bitmapData = workingBitmap.LockBits(
                new Rectangle(0, 0, workingBitmap.Width, workingBitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            // 直接使用FromPixelData创建Mat，避免额外的拷贝
            Mat mat = Mat.FromPixelData(workingBitmap.Height, workingBitmap.Width, MatType.CV_8UC3, bitmapData.Scan0, bitmapData.Stride);
            
            // 只有在需要时才进行颜色空间转换
            if (mat.Channels() == 3)
            {
                // 转换BGR到RGB（OpenCV使用BGR，Bitmap使用RGB）
                Cv2.CvtColor(mat, mat, ColorConversionCodes.RGB2BGR);
            }
            
            return mat.Clone(); // 返回独立副本

        }


        public List<OCRResult> PerformOCR(Bitmap img)
        {


            LastErrorMessage = string.Empty;

            List<OCRResult> results;
            using (Mat cvImg = BitmapToMat(img)) 
            {
                int resize_w;
                int resize_h;
                float ratio_h;
                float ratio_w;

                var detInput = PreprocessDetectionImage(cvImg, out resize_w, out resize_h, out ratio_h, out ratio_w);

                var detBoxes = RunDetection(detInput, resize_h, resize_w, cvImg.Width, cvImg.Height);

                if (detBoxes.Count == 0)
                {
                    LastErrorMessage = "未能检测到任何文本区域";
                    return new List<OCRResult>();
                }

                List<OCRResult> ocrResults = new List<OCRResult>();
                foreach (var box in detBoxes)
                {
                    OpenCvSharp.Rect roi = new OpenCvSharp.Rect(box.XMin, box.YMin, box.Width, box.Height);
                    roi = roi.Intersect(new OpenCvSharp.Rect(0, 0, cvImg.Width, cvImg.Height));
                    if (roi.Width <= 0 || roi.Height <= 0)
                        continue;

                    using (Mat cropped = new Mat(cvImg, roi))
                    {
                        var (text, confidence) = RunRecognition(cropped);




                        // save figures for debugging
                        string resultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "result");
                        if (!Directory.Exists(resultDir))
                        {
                            Directory.CreateDirectory(resultDir);
                        }
                        // 保存裁剪后的图像，以便调试
                        // string fileName = $"{Guid.NewGuid()}.png";
                        // string filePath = Path.Combine(resultDir, fileName);
                        // cropped.SaveImage(filePath);


                        if (!string.IsNullOrEmpty(text))
                        {
                            OCRResult ocrResult = _ocrResultPool.Rent();
                            ocrResult.Text = text;
                            ocrResult.Confidence = confidence;
                            ocrResult.Box = box;
                            ocrResults.Add(ocrResult);
                        }
                    }
                }

                results = FormatResults(ocrResults);
            }

            // 在 OCR 处理完成后立即清理
            CleanupAfterOCR();
                
            return results;
        
        }


        private List<OCRResult> FormatResults(List<OCRResult> results)
        {
            // 排列 ocrResults
            var formattedResults = new List<OCRResult>();
            float lineThreshold = 16.0f;
            List<OCRResult> currentLine = new List<OCRResult>();

            // 按 YMin 排序
            results = results.OrderBy(r => r.Box.YMin).ToList();

            foreach (var result in results)
            {
                if (currentLine.Count == 0)
                {
                    currentLine.Add(result);
                    continue;
                }

                var lastResult = currentLine.Last();
                if (Math.Abs(result.Box.YMin - lastResult.Box.YMin) < lineThreshold &&
                    Math.Abs(result.Box.YMax - lastResult.Box.YMax) < lineThreshold)
                {
                    currentLine.Add(result);
                }
                else
                {
                    ProcessCurrentLine(currentLine, formattedResults);
                    currentLine = new List<OCRResult> { result };
                }
            }

            if (currentLine.Count > 0)             // 处理最后一行
            {
                ProcessCurrentLine(currentLine, formattedResults);
            }

            return formattedResults;
        }

        private void ProcessCurrentLine(List<OCRResult> currentLine, List<OCRResult> formattedResults)
        {
            var sortedLine = currentLine
                .OrderBy(r => (r.Box.XMin + r.Box.XMax) / 2.0f)
                .ToList();

            if (sortedLine.Count > 0)
            {
                var mergedResult = sortedLine[0];
                for (int i = 1; i < sortedLine.Count; i++)
                {
                    mergedResult.Text += "    " + sortedLine[i].Text;
                    mergedResult.Box.XMax = sortedLine[i].Box.XMax;
                }
                formattedResults.Add(mergedResult);
            }
        }

        private float[] PreprocessDetectionImage(Mat img, out int resize_w, out int resize_h, out float ratio_h, out float ratio_w)
        {
            Mat resize_img = null!;
            ResizeImageType0(img, out resize_img, limit_type_, limit_side_len_, out ratio_h, out ratio_w, use_tensorrt_);
            Normalize(ref resize_img, det_mean_, det_scale_, det_is_scale_);

            resize_w = resize_img.Cols;
            resize_h = resize_img.Rows;
            
            int input_size = 1 * 3 * resize_h * resize_w;
            
            // 直接创建最终大小的数组，避免额外的拷贝操作
            float[] result = new float[input_size];
            Permute(resize_img, result);
            return result;

        }

        private void ResizeImageType0(Mat img, out Mat resize_img, string limit_type, int limit_side_len, out float ratio_h, out float ratio_w, bool use_tensorrt)
        {
            int w = img.Width;
            int h = img.Height;
            float ratio = 1.0f;

            if (limit_type == "min")
            {
                int min_wh = Math.Min(h, w);
                if (min_wh < limit_side_len)
                {
                    ratio = (float)limit_side_len / (float)min_wh;
                }
            }
            else
            {
                int max_wh = Math.Max(h, w);
                if (max_wh > limit_side_len)
                {
                    ratio = (float)limit_side_len / (float)max_wh;
                }
            }

            int resize_h_calc = (int)((float)h * ratio);
            int resize_w_calc = (int)((float)w * ratio);

            // 优化：减少重复计算
            resize_h_calc = Math.Max(((resize_h_calc + 31) / 32) * 32, 32);
            resize_w_calc = Math.Max(((resize_w_calc + 31) / 32) * 32, 32);

            // 使用对象池获取Mat对象
            resize_img = _matPool.Rent(resize_h_calc, resize_w_calc, img.Type());

            // 如果尺寸没有变化，直接复制原图像数据
            if (resize_h_calc == h && resize_w_calc == w)
            {
                img.CopyTo(resize_img);
            }
            else
            {
                Cv2.Resize(img, resize_img, new OpenCvSharp.Size(resize_w_calc, resize_h_calc), 0, 0, InterpolationFlags.Linear);
            }

            ratio_h = (float)resize_h_calc / (float)h;
            ratio_w = (float)resize_w_calc / (float)w;
        }

        private void Normalize(ref Mat im, List<float> mean, List<float> scale, bool is_scale)
        {
            double e = 1.0;
            if (is_scale)
            {
                e /= 255.0;
            }

            // 使用分离的操作来避免Scalar类型问题
            im.ConvertTo(im, MatType.CV_32FC3, e);
            
            // 分别处理每个通道的归一化
            Mat[] channels = Cv2.Split(im);
            for (int i = 0; i < channels.Length && i < mean.Count && i < scale.Count; i++)
            {
                Cv2.Subtract(channels[i], new Scalar(mean[i]), channels[i]);
                Cv2.Multiply(channels[i], new Scalar(scale[i]), channels[i]);
            }
            Cv2.Merge(channels, im);
            
            // 清理临时Mat对象
            foreach (var channel in channels)
            {
                channel.Dispose();
            }
        }

        private unsafe void Permute(Mat im, float[] data)
        {
            int h = im.Height;
            int w = im.Width;
            int c = im.Channels();
            
            // 固定数组以获得指针访问
            fixed (float* dataPtr = data)
            {
                // 获取Mat的数据指针
                IntPtr matDataPtr = im.Data;
                float* matPtr = (float*)matDataPtr.ToPointer();
                
                // 计算步长（每行的字节数除以float大小）
                int step = (int)(im.Step() / sizeof(float));
                
                // 优化的内存拷贝：按通道重新排列数据
                // 使用指针操作避免边界检查和函数调用开销
                for (int k = 0; k < c; k++)
                {
                    float* channelDataPtr = dataPtr + k * h * w;
                    
                    for (int i = 0; i < h; i++)
                    {
                        float* rowPtr = matPtr + i * step;
                        float* destRowPtr = channelDataPtr + i * w;
                        
                        for (int j = 0; j < w; j++)
                        {
                            destRowPtr[j] = rowPtr[j * c + k];
                        }
                    }
                }
            }
        }




        private List<BoundingBox> RunDetection(float[] inputData, int resize_h, int resize_w, int original_w, int original_h)
        {
            // 直接使用输入数据创建tensor，避免额外的数组创建和拷贝
            var detInputTensor = new DenseTensor<float>(inputData, new[] { 1, 3, resize_h, resize_w });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(detectionSession.InputMetadata.Keys.First(), detInputTensor) };

            using (var results = detectionSession.Run(inputs))
            {
                var outputName = detectionSession.OutputMetadata.Keys.First();
                var outputTensor = results.First(r => r.Name == outputName).AsTensor<float>();

                List<BoundingBox> boxes = PostProcessDetectionOutput(outputTensor, original_w, original_h);

                return boxes;
            }
        }

        private List<BoundingBox> PostProcessDetectionOutput(Tensor<float> outputTensor, int original_w, int original_h)
        {

            int n = outputTensor.Dimensions[0];
            int c = outputTensor.Dimensions[1];
            int h = outputTensor.Dimensions[2];
            int w = outputTensor.Dimensions[3];

            float[] outputData = outputTensor.ToArray();

            if (c != 1)
            {
                LastErrorMessage = $"检测模型输出通道数异常：期望为1，实际为{c}。将仅使用第一个通道。";
                // 直接截取第一个通道的数据，避免额外的数组分配
                float[] firstChannelData = new float[h * w];
                Array.Copy(outputData, 0, firstChannelData, 0, h * w);
                outputData = firstChannelData;
            }

            // 使用对象池优化内存分配
            Mat probMap = _matPool.Rent(h, w, MatType.CV_32FC1);
            Mat resizedProbMap = _matPool.Rent(original_h, original_w, MatType.CV_32FC1);
            Mat binaryMap = _matPool.Rent(original_h, original_w, MatType.CV_8UC1);
            
            try
            {
                // 使用 Marshal.Copy 复制 float[] 到 Mat.Data
                GCHandle handle = GCHandle.Alloc(outputData, GCHandleType.Pinned);
                try
                {
                    Marshal.Copy(outputData, 0, probMap.Data, outputData.Length);
                }
                finally
                {
                    handle.Free();
                }

                // 添加调试信息
                Cv2.MinMaxLoc(probMap, out double minVal, out double maxVal, out _, out _);
                double meanVal = Cv2.Mean(probMap).Val0;

                Cv2.Exp(-probMap, probMap);
                Cv2.Add(probMap, 1.0, probMap);
                Cv2.Divide(1.0, probMap, probMap);   //应用 Sigmoid 激活

                Cv2.Resize(probMap, resizedProbMap, new OpenCvSharp.Size(original_w, original_h)); //概率图重采样

                Cv2.Threshold(resizedProbMap, binaryMap, 1-det_db_thresh_, 255, ThresholdTypes.Binary); //概率图二值化

                if (use_dilation_)
                {
                    using (Mat element = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(2, 2)))
                    {
                        Cv2.Dilate(binaryMap, binaryMap, element);
                    }
                }

                OpenCvSharp.Point[][] contours;
                HierarchyIndex[] hierarchy;

                binaryMap.ConvertTo(binaryMap, MatType.CV_8UC1);
                Cv2.FindContours(binaryMap, out contours, out hierarchy, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

                List<BoundingBox> boxes = new List<BoundingBox>();
                foreach (var contour in contours)
                {
                    if (contour.Length <= 3)
                    {
                        continue;
                    }

                    RotatedRect rotatedRect = Cv2.MinAreaRect(contour);
                    float ssid;
                    var box = GetMiniBoxes(rotatedRect, out ssid);

                    if (ssid < 16) //如果一个区域的最长边小于16像素
                    {
                        continue;
                    }

                    float score;
                    score = PolygonScoreAcc(contour, resizedProbMap);


                    if (score < det_db_box_thresh_)
                    {
                        LastErrorMessage = $"检测到的区域评分({score})低于阈值({det_db_box_thresh_})";
                        continue;
                    }

                    RotatedRect clipRect = UnClip(rotatedRect, det_db_unclip_ratio_);

                    if (clipRect.Size.Height < 1.001 && clipRect.Size.Width < 1.001)
                    {
                        continue;
                    }

                    CvPoint2f[] clipPoints = clipRect.Points();
                    float xmin = clipPoints.Min(p => p.X);
                    float ymin = clipPoints.Min(p => p.Y);
                    float xmax = clipPoints.Max(p => p.X);
                    float ymax = clipPoints.Max(p => p.Y);

                    BoundingBox boundingBox = _boundingBoxPool.Rent();
                    boundingBox.XMin = (int)Math.Round(xmin);
                    boundingBox.YMin = (int)Math.Round(ymin);
                    boundingBox.XMax = (int)Math.Round(xmax);
                    boundingBox.YMax = (int)Math.Round(ymax);

                    boundingBox.XMin = Clamp(boundingBox.XMin, 0, original_w - 1);
                    boundingBox.YMin = Clamp(boundingBox.YMin, 0, original_h - 1);
                    boundingBox.XMax = Clamp(boundingBox.XMax, 0, original_w - 1);
                    boundingBox.YMax = Clamp(boundingBox.YMax, 0, original_h - 1);

                    boxes.Add(boundingBox);
                }

                if (boxes.Count == 0)
                {
                    LastErrorMessage = "未能找到任何有效的文本区域";
                }

                return boxes;
            }
            finally
            {
                // 归还Mat对象到对象池
                _matPool.Return(probMap);
                _matPool.Return(resizedProbMap);
                _matPool.Return(binaryMap);
            }
        }

        private CvPoint2f[] GetMiniBoxes(RotatedRect box, out float ssid)
        {
            ssid = Math.Max(box.Size.Width, box.Size.Height);

            CvPoint2f[] points = box.Points();
            Array.Sort(points, (a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));

            CvPoint2f[] reorder = new CvPoint2f[4];
            reorder[0] = points[0];
            reorder[3] = points[3];

            if (points[1].Y <= points[2].Y)
            {
                reorder[1] = points[1];
                reorder[2] = points[2];
            }
            else
            {
                reorder[1] = points[2];
                reorder[2] = points[1];
            }

            return reorder;
        }



        private RotatedRect UnClip(RotatedRect rotatedRect, float unclipRatio)   //原版 UnClip
        {
            // 获取旋转矩形的四个顶点 
            CvPoint2f[] points = rotatedRect.Points();

            // 计算矩形的周长 
            float perimeter = 0;
            for (int i = 0; i < 4; i++)
            {
                perimeter += L2Norm(points[i] - points[(i + 1) % 4]);
            }

            // 计算矩形的面积 
            float area = (float)Cv2.ContourArea(points);

            // 计算膨胀距离 
            float distance = area * unclipRatio / perimeter;

            // 创建一个稍大的矩形 
            CvPoint2f[] expandedPoints = new CvPoint2f[8]; // 增加空间以存储8个点 
            for (int i = 0; i < 4; i++)
            {
                CvPoint2f vector = points[(i + 1) % 4] - points[i];

                float norm = L2Norm(vector);
                CvPoint2f unit = norm != 0 ? new CvPoint2f(vector.X / norm, vector.Y / norm) : new CvPoint2f(0, 0);
                CvPoint2f perpendicular = new CvPoint2f(-unit.Y, unit.X);

                CvPoint2f distanceUnit = new CvPoint2f(distance * unit.X, distance * unit.Y);
                CvPoint2f distancePerpendicular = new CvPoint2f(distance * perpendicular.X, distance * perpendicular.Y);

                // 计算基础扩展点 
                float baseY = points[i].Y - distanceUnit.Y + distancePerpendicular.Y;
                float baseX = points[i].X - distanceUnit.X + distancePerpendicular.X;


                // 保存扩展后的点 
                expandedPoints[i * 2] = new CvPoint2f(baseX, baseY); // 上侧点 
                expandedPoints[i * 2 + 1] = new CvPoint2f(baseX, baseY); // 下侧点 
            }

            // 使用扩展后的点创建新的轮廓并计算最小外接旋转矩形 
            RotatedRect expandedRotatedRect = Cv2.MinAreaRect(expandedPoints);

            return expandedRotatedRect;
        }


        private float L2Norm(CvPoint2f point)
        {
            return (float)Math.Sqrt(point.X * point.X + point.Y * point.Y);
        }

        private int Clamp(int x, int min, int max)
        {
            if (x > max) return max;
            if (x < min) return min;
            return x;
        }


        private float PolygonScoreAcc(OpenCvSharp.Point[] contour, Mat pred)
        {
            int width = pred.Cols;
            int height = pred.Rows;

            // 使用对象池优化内存分配
            Mat mask = _matPool.Rent(height, width, MatType.CV_8UC1);
            try
            {
                // 初始化为黑色
                mask.SetTo(Scalar.Black);
                // 填充多边形掩码
                Cv2.FillConvexPoly(mask, contour, Scalar.White);

                Scalar meanVal = Cv2.Mean(pred, mask);
                return (float)meanVal.Val0;
            }
            finally
            {
                _matPool.Return(mask);
            }
        }




        private (string, float) RunRecognition(Mat img)
        {
            Mat resizeImg;
            ResizeRecognitionImage(img, out resizeImg);
                
            try
            {
                if (resizeImg == null || resizeImg.Empty())
                {
                    LastErrorMessage = "调整识别图像大小失败";
                    return ("", 0f);
                }

                NormalizeRecognitionImage(ref resizeImg);

                int imgC = 3;
                int imgH = resizeImg.Rows;
                int imgW = resizeImg.Cols;
                int inputSize = imgC * imgH * imgW;
                
                // 直接创建最终大小的数组，避免使用数组池和额外拷贝
                float[] inputData = new float[inputSize];
                Permute(resizeImg, inputData);

                var inputTensor = new DenseTensor<float>(inputData, new int[] { 1, 3, imgH, imgW });

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor(recognitionSession.InputMetadata.Keys.First(), inputTensor)
                };

                using (var results = recognitionSession.Run(inputs))
                {
                    var outputName = recognitionSession.OutputMetadata.Keys.First();
                    var outputTensor = results.First(r => r.Name == outputName).AsTensor<float>();

                    if (outputTensor == null)
                    {
                        LastErrorMessage = "识别模型未能生成有效输出";
                        return ("", 0f);
                    }

                    var (strRes, score) = PostProcessRecognitionOutput(outputTensor);

                    if (string.IsNullOrEmpty(strRes))
                     {
                         LastErrorMessage = "识别结果为空";
                         return ("", score);
                     }

                     return (strRes, score);
                 }
            }
            finally
            {
                // 确保resizeImg归还到对象池
                if (resizeImg != null)
                {
                    _matPool.Return(resizeImg);
                }
            }
        }

        private (string, float) PostProcessRecognitionOutput(Tensor<float> outputTensor)
        {
            int[] shape = outputTensor.Dimensions.ToArray();
            float[] outputData = outputTensor.ToArray();

            int batchSize = shape[0];
            int timeSteps = shape[1];
            int classNum = shape[2];

            string strRes = "";
            int argmaxIdx;
            int lastIndex = 0;
            float score = 0f;
            int count = 0;

            for (int n = 0; n < timeSteps; n++)
            {
                int start = n * classNum;
                var slice = outputData.Skip(start).Take(classNum).ToArray();

                argmaxIdx = ArgMax(slice);
                float maxValue = slice.Max();

                if (argmaxIdx > 0 && (!(n > 0 && argmaxIdx == lastIndex)))
                {
                    score += maxValue;
                    count += 1;
                    if (argmaxIdx < recLabels.Count) // 防止索引越界
                        strRes += recLabels[argmaxIdx];
                    else
                        strRes += "?"; // 未知字符
                }
                lastIndex = argmaxIdx;
            }
            score = count > 0 ? score / count : 0f;
            return (strRes, score);
        }

        private void ResizeRecognitionImage(Mat srcImg, out Mat resizeImg)
        {
            int imgH = rec_img_h_;
            int imgW = rec_img_w_;

            float actual_ratio = (float)srcImg.Width / srcImg.Height;
            int resizeW = (int)Math.Ceiling(imgH * actual_ratio);

            // 使用对象池优化内存分配
            resizeImg = _matPool.Rent(imgH, resizeW, srcImg.Type());
            Cv2.Resize(srcImg, resizeImg, new OpenCvSharp.Size(resizeW, imgH), 0, 0, InterpolationFlags.Linear);
        }

        private void NormalizeRecognitionImage(ref Mat img)
        {
            double e = rec_is_scale_ ? 1.0 / 255.0 : 1.0;
            img.ConvertTo(img, MatType.CV_32FC3, e);

            var mean = rec_mean_.ToArray();
            var scale = rec_scale_.ToArray();

            Cv2.Subtract(img, new Scalar(mean[0], mean[1], mean[2]), img);
            Cv2.Multiply(img, new Scalar(scale[0], scale[1], scale[2]), img); 

        }

        private int ArgMax(float[] array)
        {
            return Array.IndexOf(array, array.Max());
        }





        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    detectionSession?.Dispose();
                    recognitionSession?.Dispose();
                    _matPool?.Clear(); // 清理Mat对象池
                    _boundingBoxPool?.Clear(); // 清理BoundingBox对象池
                    _ocrResultPool?.Clear(); // 清理OCRResult对象池
                }
                disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void CleanupAfterOCR()
        {
            // 优化内存管理：让GC自然进行，避免强制清理
            // 仅清理对象池中的过期对象
            _matPool?.Clear();
            _boundingBoxPool?.Clear();
            _ocrResultPool?.Clear();
            
            // 清理数组池中的大数组（如果有的话）
            // ArrayPool会自动管理内存，无需手动干预
        }
    }
}
