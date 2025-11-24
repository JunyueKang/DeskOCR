using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JiebaNet.Segmenter;

namespace OcrApp.Services
{
    /// <summary>
    /// JiebaSegmenter 管理器，负责初始化和管理 JiebaSegmenter 实例
    /// </summary>
    public class JiebaSegmenterManager
    {
        static JiebaSegmenterManager? _instance;
        static readonly object _lock = new object();
        JiebaSegmenter? _segmenter;
        bool _isInitialized = false;
        string? _lastError;

        /// <summary>
        /// 获取单例实例
        /// </summary>
        public static JiebaSegmenterManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new JiebaSegmenterManager();
                        }
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// 私有构造函数
        /// </summary>
        private JiebaSegmenterManager()
        {
            // 构造函数中不进行初始化，延迟到第一次使用时
        }

        /// <summary>
        /// 初始化 JiebaSegmenter
        /// </summary>
        /// <returns>初始化是否成功</returns>
        public bool Initialize()
        {
            if (_isInitialized && _segmenter != null)
            {
                return true;
            }

            lock (_lock)
            {
                if (_isInitialized && _segmenter != null)
                {
                    return true;
                }

                try
                {
                    // 严格按照用户提供的工作示例进行初始化
                    // 设置Jieba资源文件路径
                    var currentDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
                    var baseResourcesDir = Path.Combine(currentDir, "Resources");
                    var resourceDir = Path.Combine(baseResourcesDir, "jieba");

                    JiebaNet.Segmenter.ConfigManager.ConfigFileBaseDir = resourceDir;
                    
                    // 初始化jieba分词器
                    _segmenter = new JiebaSegmenter();
                    
                    // 测试分词器是否正常工作
                    var testResult = _segmenter.Cut("测试");
                    if (testResult == null || !testResult.Any())
                    {
                        throw new InvalidOperationException("JiebaSegmenter 初始化后无法正常分词");
                    }

                    _isInitialized = true;
                    _lastError = null;
                    return true;
                }
                catch (Exception ex)
                {
                    _lastError = $"JiebaSegmenter 初始化失败: {ex.Message}";
                    _segmenter = null;
                    _isInitialized = false;
                    throw new InvalidOperationException($"JiebaSegmenter 初始化失败: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// 对文本进行分词
        /// </summary>
        /// <param name="text">要分词的文本</param>
        /// <param name="cutAll">是否使用全模式分词</param>
        /// <returns>分词结果列表</returns>
        public List<string> SegmentText(string text, bool cutAll = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<string>();
            }

            // 确保分词器已初始化
            if (!_isInitialized || _segmenter == null)
            {
                Initialize(); // 如果初始化失败，会抛出异常
            }

            try
            {
                // 使用JiebaSegmenter分词
                var segments = _segmenter!.Cut(text, cutAll: cutAll);
                return segments.ToList();
            }
            catch (Exception ex)
            {
                _lastError = $"分词过程中发生错误: {ex.Message}";
                throw new InvalidOperationException($"分词过程中发生错误: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 获取最后一次错误信息
        /// </summary>
        public string? LastError => _lastError;

        /// <summary>
        /// 检查分词器是否已成功初始化
        /// </summary>
        public bool IsInitialized => _isInitialized && _segmenter != null;
    }
}