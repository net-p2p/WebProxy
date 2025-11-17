using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace WebProxy.DiyPolicy
{
    public interface IYarpPolicyService
    {
        public YarpPolicyConfig PolicyConfig { get; }
        public AuthorizationPolicyConfig AuthorizationPolicy { get; }
        public CorsPolicyConfig CorsPolicy { get; }
        public OutputCachePolicyConfig OutputCachePolicy { get; }
        public RateLimiterPolicyConfig RateLimiterPolicy { get; }
        public TimeoutPolicyConfig TimeoutPolicy { get; }

        AuthPolicy GetAuthorizationPolicy(string policyName);
        CorsPolicy GetCorsPolicy(string policyName);
        OutputCachePolicy GetOutputCachePolicy(string policyName);
        RateLimitPolicy GetRateLimiterPolicy(string policyName);
        TimeoutPolicy GetTimeoutPolicy(string policyName);
    }

    public class YarpPolicyService : IYarpPolicyService
    {
        private readonly YarpPolicyConfig _config;

        public YarpPolicyService(IOptionsSnapshot<YarpPolicyConfig> options)
        {
            _config = options.Value;
        }

        public YarpPolicyConfig PolicyConfig => _config;
        public AuthorizationPolicyConfig AuthorizationPolicy => _config.AuthorizationPolicy;
        public CorsPolicyConfig CorsPolicy => _config.CorsPolicy;
        public OutputCachePolicyConfig OutputCachePolicy => _config.OutputCachePolicy;
        public RateLimiterPolicyConfig RateLimiterPolicy => _config.RateLimiterPolicy;
        public TimeoutPolicyConfig TimeoutPolicy => _config.TimeoutPolicy;

        public AuthPolicy GetAuthorizationPolicy(string policyName)
        {
            return _config.AuthorizationPolicy.Policies.TryGetValue(policyName, out var policy) ? policy : null;
        }

        public CorsPolicy GetCorsPolicy(string policyName)
        {
            return _config.CorsPolicy.Policies.TryGetValue(policyName, out var policy) ? policy : null;
        }

        public OutputCachePolicy GetOutputCachePolicy(string policyName)
        {
            return _config.OutputCachePolicy.Policies.TryGetValue(policyName, out var policy) ? policy : null;
        }

        public RateLimitPolicy GetRateLimiterPolicy(string policyName)
        {
            return _config.RateLimiterPolicy.Policies.TryGetValue(policyName, out var policy) ? policy : null;
        }

        public TimeoutPolicy GetTimeoutPolicy(string policyName)
        {
            return _config.TimeoutPolicy.Policies.TryGetValue(policyName, out var policy) ? policy : null;
        }
    }
}
