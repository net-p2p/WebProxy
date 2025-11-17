using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;
using Tool;

namespace WebProxy.DiyPolicy
{
    public static class YarpPolicyExtensions
    {
        private static IYarpPolicyService YarpPolicy => ObjectExtension.Provider.GetRequiredService<IYarpPolicyService>();

        /// <summary>
        /// Adds YARP-related policy services and configures authorization, CORS, output caching, and rate limiting
        /// policies based on the specified configuration.
        /// </summary>
        /// <remarks>This method registers and configures policy services for YARP (Yet Another Reverse
        /// Proxy) using settings from the provided configuration. It should be called during application startup to
        /// ensure that all relevant policies are available for dependency injection and middleware
        /// configuration.</remarks>
        /// <param name="services">The service collection to which the YARP policy services and policies will be added.</param>
        /// <param name="configuration">The application configuration containing the YARP policy settings. Must include a 'YarpPolicy' section.</param>
        /// <returns>The <see cref="IServiceCollection"/> instance with YARP policy services and policies registered.</returns>
        public static IServiceCollection AddYarpPolicies(this IServiceCollection services, IConfiguration configuration)
        {
            // 注册配置服务
            services.Configure<YarpPolicyConfig>(configuration.GetSection("YarpPolicy"));
            services.AddSingleton<IYarpPolicyService, YarpPolicyService>();

            // 配置授权策略
            ConfigureAuthorizationPolicies(services);

            // 配置CORS策略
            ConfigureCorsPolicies(services);

            // 配置输出缓存策略
            ConfigureOutputCachePolicies(services);

            // 配置限流策略
            ConfigureRateLimiterPolicies(services);

            // 配置超时策略
            ConfigureTimeoutPolicies(services);

            return services;
        }

        /// <summary>
        /// Adds and configures YARP-related middleware policies, including CORS, routing, authorization, rate limiting,
        /// output caching, and request timeouts, to the application's request pipeline.
        /// </summary>
        /// <remarks>This method should be called during application startup to ensure that all required
        /// YARP middleware components are registered in the correct order. The method applies CORS policies if
        /// configured, and adds middleware for routing, authorization, rate limiting, output caching, and request
        /// timeouts. The order of middleware registration is important for correct request processing.</remarks>
        /// <param name="app">The <see cref="IApplicationBuilder"/> instance to configure with YARP policies. Cannot be null.</param>
        /// <returns>The <see cref="IApplicationBuilder"/> instance with the YARP policies configured.</returns>
        public static IApplicationBuilder UseYarpPolicies(this IApplicationBuilder app)
        {
            app.UseAuthorization();
            app.UseCors();
            app.UseOutputCache();
            app.UseRateLimiter();
            app.UseRequestTimeouts(); // 使用请求超时中间件

            return app;
        }

        private static void ConfigureAuthorizationPolicies(IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                AuthorizationPolicyConfig config = YarpPolicy.AuthorizationPolicy;
                foreach (var policyEntry in config.Policies)
                {
                    if (!policyEntry.Value.Enabled) continue;

                    options.AddPolicy(policyEntry.Key, policy =>
                    {
                        if (policyEntry.Value.RequireAuthenticatedUser)
                        {
                            policy.RequireAuthenticatedUser();
                        }

                        if (policyEntry.Value.AuthenticationSchemes?.Length > 0)
                        {
                            policy.AuthenticationSchemes = policyEntry.Value.AuthenticationSchemes;
                        }

                        foreach (var claim in policyEntry.Value.RequireClaim)
                        {
                            policy.RequireClaim(claim.Type, claim.Value);
                        }

                        foreach (var role in policyEntry.Value.RequireRole)
                        {
                            policy.RequireRole(role);
                        }
                    });
                }
            });
        }

        private static void ConfigureCorsPolicies(IServiceCollection services)
        {
            services.AddCors(options =>
            {
                CorsPolicyConfig config = YarpPolicy.CorsPolicy;
                foreach (var policyEntry in config.Policies)
                {
                    if (!policyEntry.Value.Enabled) continue;

                    options.AddPolicy(policyEntry.Key, policy =>
                    {
                        if (policyEntry.Value.AllowAnyOrigin)
                        {
                            policy.AllowAnyOrigin();
                        }
                        else if (policyEntry.Value.AllowedOrigins?.Length > 0)
                        {
                            policy.WithOrigins(policyEntry.Value.AllowedOrigins);
                        }

                        if (policyEntry.Value.AllowAnyMethod)
                        {
                            policy.AllowAnyMethod();
                        }

                        if (policyEntry.Value.AllowAnyHeader)
                        {
                            policy.AllowAnyHeader();
                        }

                        if (policyEntry.Value.ExposeHeaders?.Length > 0)
                        {
                            policy.WithExposedHeaders(policyEntry.Value.ExposeHeaders);
                        }

                        if (policyEntry.Value.AllowCredentials)
                        {
                            policy.AllowCredentials();
                        }

                        if (policyEntry.Value.MaxAge > 0)
                        {
                            policy.SetPreflightMaxAge(TimeSpan.FromSeconds(policyEntry.Value.MaxAge));
                        }
                    });
                }
            });
        }

        private static void ConfigureOutputCachePolicies(IServiceCollection services)
        {
            services.AddOutputCache(options =>
            {
                OutputCachePolicyConfig config = YarpPolicy.OutputCachePolicy;
                foreach (var policyEntry in config.Policies)
                {
                    if (!policyEntry.Value.Enabled) continue;

                    options.AddPolicy(policyEntry.Key, policy =>
                    {
                        policy.Expire(TimeSpan.FromSeconds(policyEntry.Value.Duration));

                        if (policyEntry.Value.VaryByQueryKeys?.Length > 0)
                        {
                            if (policyEntry.Value.VaryByQueryKeys.Contains("*"))
                            {
                                policy.SetVaryByQuery("*");
                            }
                            else
                            {
                                policy.SetVaryByQuery(policyEntry.Value.VaryByQueryKeys);
                            }
                        }

                        if (policyEntry.Value.VaryByHeader?.Length > 0)
                        {
                            policy.SetVaryByHeader(policyEntry.Value.VaryByHeader);
                        }
                    });
                }
            });
        }

        private static void ConfigureRateLimiterPolicies(IServiceCollection services)
        {
            services.AddRateLimiter(options =>
            {
                RateLimiterPolicyConfig config = YarpPolicy.RateLimiterPolicy;
                foreach (var policyEntry in config.Policies)
                {
                    if (!policyEntry.Value.Enabled) continue;

                    options.AddFixedWindowLimiter(policyEntry.Key, opt =>
                    {
                        opt.PermitLimit = policyEntry.Value.PermitLimit;
                        opt.Window = TimeSpan.FromSeconds(policyEntry.Value.Window);
                        opt.QueueProcessingOrder = System.Threading.RateLimiting.QueueProcessingOrder.OldestFirst;
                        opt.QueueLimit = policyEntry.Value.QueueLimit;
                        opt.AutoReplenishment = policyEntry.Value.AutoReplenishment;
                    });
                }
            });
        }

        private static void ConfigureTimeoutPolicies(IServiceCollection services)
        {
            // 添加请求超时服务
            services.AddRequestTimeouts(options =>
            {
                TimeoutPolicyConfig config = YarpPolicy.TimeoutPolicy;
                foreach (var policyEntry in config.Policies)
                {
                    if (!policyEntry.Value.Enabled) continue;

                    options.AddPolicy(policyEntry.Key, TimeSpan.FromSeconds(policyEntry.Value.Timeout));
                }

                // 设置默认超时策略
                if (!string.IsNullOrEmpty(config.DefaultPolicy) &&
                    config.Policies.TryGetValue(config.DefaultPolicy, out var defaultPolicy) &&
                    defaultPolicy.Enabled && options.Policies.TryGetValue(config.DefaultPolicy, out var policy))
                {
                    options.DefaultPolicy = policy;
                }
                else
                {
                    options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy() { Timeout = TimeSpan.FromSeconds(100) };
                }

                for (int i = 1; i <= 60; i++)
                {
                    if (!options.Policies.ContainsKey($"{i}s"))
                    {
                        options.AddPolicy($"{i}s", TimeSpan.FromSeconds(i));
                    }
                    if (!options.Policies.ContainsKey($"{i}m"))
                    {
                        options.AddPolicy($"{i}m", TimeSpan.FromMinutes(i));
                    }
                }
            });
        }
    }
}
