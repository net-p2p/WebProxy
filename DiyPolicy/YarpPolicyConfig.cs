using System;
using System.Collections.Generic;

namespace WebProxy.DiyPolicy
{
    public class YarpPolicyConfig
    {
        public AuthorizationPolicyConfig AuthorizationPolicy { get; set; } = new();
        public CorsPolicyConfig CorsPolicy { get; set; } = new();
        public OutputCachePolicyConfig OutputCachePolicy { get; set; } = new();
        public RateLimiterPolicyConfig RateLimiterPolicy { get; set; } = new();
        public TimeoutPolicyConfig TimeoutPolicy { get; set; } = new();
    }

    public class AuthorizationPolicyConfig
    {
        public string DefaultPolicy { get; set; } = string.Empty;
        public Dictionary<string, AuthPolicy> Policies { get; set; } = [];
    }

    public class AuthPolicy
    {
        public bool Enabled { get; set; }
        public string[] AuthenticationSchemes { get; set; } = [];
        public bool RequireAuthenticatedUser { get; set; }
        public List<ClaimRequirement> RequireClaim { get; set; } = [];
        public List<string> RequireRole { get; set; } = [];
    }

    public class ClaimRequirement
    {
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class CorsPolicyConfig
    {
        public string DefaultPolicy { get; set; } = string.Empty;
        public Dictionary<string, CorsPolicy> Policies { get; set; } = [];
    }

    public class CorsPolicy
    {
        public bool Enabled { get; set; }
        public bool AllowAnyOrigin { get; set; }
        public string[] AllowedOrigins { get; set; } = [];
        public bool AllowAnyMethod { get; set; }
        public bool AllowAnyHeader { get; set; }
        public string[] ExposeHeaders { get; set; } = [];
        public int MaxAge { get; set; }
        public bool AllowCredentials { get; set; }
    }

    public class OutputCachePolicyConfig
    {
        public string DefaultPolicy { get; set; } = string.Empty;
        public Dictionary<string, OutputCachePolicy> Policies { get; set; } = [];
    }

    public class OutputCachePolicy
    {
        public bool Enabled { get; set; }
        public int Duration { get; set; }
        public string[] VaryByQueryKeys { get; set; } = [];
        public string[] VaryByHeader { get; set; } = [];
    }

    public class RateLimiterPolicyConfig
    {
        public string DefaultPolicy { get; set; } = string.Empty;
        public Dictionary<string, RateLimitPolicy> Policies { get; set; } = [];
    }

    public class RateLimitPolicy
    {
        public bool Enabled { get; set; }
        public int PermitLimit { get; set; } = 100;
        public int Window { get; set; } = 60;
        public int QueueLimit { get; set; }
        public bool AutoReplenishment { get; set; } = true;
    }

    public class TimeoutPolicyConfig
    {
        public string DefaultPolicy { get; set; } = string.Empty;
        public Dictionary<string, TimeoutPolicy> Policies { get; set; } = [];
    }

    public class TimeoutPolicy
    {
        public bool Enabled { get; set; }
        public int Timeout { get; set; } = 30;
    }
}
