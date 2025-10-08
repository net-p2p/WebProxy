using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WebProxy.Extensions
{
    public class DynamicConfiguration : IConfiguration
    {
        private readonly IConfiguration _inner;
        private CancellationTokenSource _cts = new();
        private readonly Lock _sync = new();
        private readonly TimeSpan _debounceDelay;
        private CancellationTokenSource _debounceCts;

        public DynamicConfiguration(IConfiguration inner, TimeSpan? debounceDelay = null)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _debounceDelay = debounceDelay ?? TimeSpan.FromSeconds(1); // 默认防抖 1s
            RegisterInnerChangeCallback();
        }

        private void RegisterInnerChangeCallback()
        {
            var token = _inner.GetReloadToken();
            token.RegisterChangeCallback(_ =>
            {
                try
                {
                    TriggerReloadFromInner();
                }
                finally
                {
                    // 重新注册以继续监听后续变化
                    RegisterInnerChangeCallback();
                }
            }, state: null);
        }

        private void TriggerReloadFromInner()
        {
            using (_sync.EnterScope())
            {
                if (!_debounceCts?.TryReset() ?? true)
                {
                    _debounceCts?.Dispose();
                    _debounceCts = new CancellationTokenSource();
                }

                _debounceCts.Token.Register(Reload, this, false);
                _debounceCts.CancelAfter(_debounceDelay);
            }

            static void Reload(object state) 
            {
                if (state is DynamicConfiguration _config) _config.TriggerReload();
            }
        }

        /// <summary>
        /// 手动触发刷新。
        /// </summary>
        public void TriggerReload()
        {
            using (_sync.EnterScope())
            {
                var previous = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
                previous.Cancel();
                previous.Dispose();
            }
        }

        // 返回当前的 change token（注意：每次都会返回包装当前 CTS 的 CancellationChangeToken）
        public IChangeToken GetReloadToken() => new CancellationChangeToken(Volatile.Read(ref _cts).Token);

        // 其余 IConfiguration 转发到内层，但在写操作时触发一次刷新
        public string this[string key]
        {
            get => _inner[key];
            set
            {
                _inner[key] = value;
                TriggerReload(); // 本地写入时触发（会走合并逻辑）
            }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => _inner.GetChildren().Select(s => new DynamicConfigurationSection(s, this));

        public IConfigurationSection GetSection(string key) => new DynamicConfigurationSection(_inner.GetSection(key), this);

        // 内部的 Section wrapper：关键是它的 GetReloadToken() 返回根的 token
        private class DynamicConfigurationSection : IConfigurationSection
        {
            private readonly IConfigurationSection _inner;
            private readonly DynamicConfiguration _root;

            public DynamicConfigurationSection(IConfigurationSection inner, DynamicConfiguration root)
            {
                _inner = inner;
                _root = root;
            }

            public string Key => _inner.Key;
            public string Path => _inner.Path;

            public string Value
            {
                get => _inner.Value;
                set
                {
                    _inner.Value = value;
                    _root.TriggerReload();
                }
            }

            public string this[string key]
            {
                get => _inner[key];
                set
                {
                    _inner[key] = value;
                    _root.TriggerReload();
                }
            }

            public IEnumerable<IConfigurationSection> GetChildren() => _inner.GetChildren().Select(s => new DynamicConfigurationSection(s, _root));

            // 重要：返回 root 的 token，保证监听一致
            public IChangeToken GetReloadToken() => _root.GetReloadToken();

            public IConfigurationSection GetSection(string key) => new DynamicConfigurationSection(_inner.GetSection(key), _root);
        }
    }
}
