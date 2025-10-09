# 请参阅 https://aka.ms/customizecontainer 以了解如何自定义调试容器，以及 Visual Studio 如何使用此 Dockerfile 生成映像以更快地进行调试。

# 第一阶段：基础运行环境
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base

# 设置构建时参数和运行时环境变量
ARG APP_PORT=7080

# 设置工作目录和暴露端口
WORKDIR /app
EXPOSE ${APP_PORT}
ENV TZ=Asia/Shanghai
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV ASPNETCORE_URLS=""
ENV ASPNETCORE_HTTP_PORTS=""
ENV TLS_CIPHER_MODE=Secure

# 创建目录结构
RUN mkdir -p /app/bin /app/config /app/certs /app/Log /app/wwwroot

# 设置目录权限（使用环境变量中的APP_UID）
RUN if [ -n "$APP_UID" ]; then \
        chown -R $APP_UID:0 /app && \
        chmod -R 775 /app; \
    else \
        chmod -R 755 /app; \
    fi

# 第二阶段：构建应用程序
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Docker
WORKDIR /src

# 复制项目文件并恢复依赖
COPY ["WebProxy.csproj", "."]
RUN dotnet restore "./WebProxy.csproj"

# 复制所有源代码
COPY . .

# 构建应用程序
RUN dotnet build "./WebProxy.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 第三阶段：发布应用程序
FROM build AS publish
ARG BUILD_CONFIGURATION=Docker
RUN dotnet publish "./WebProxy.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# 第四阶段：最终生产镜像
FROM base AS final
WORKDIR /app

# 从发布阶段复制构建结果到bin目录
COPY --from=publish /app/publish ./bin/

# 清理不必要的文件
RUN find /app/bin -name "*.pdb" -delete \
    && find /app/bin -name "*.xml" -delete \
    && find /app/bin -name "*.config" -delete \
    && rm -rf /app/bin/certs 2>/dev/null || true \
    && rm -rf /app/bin/runtimes 2>/dev/null || true

# 移动配置文件到config目录（使用条件判断）
USER root
RUN if [ -f ./bin/appsettings.json ]; then mv ./bin/appsettings.json /app/config/; fi && \
    if ls ./bin/appsettings.*.json 1> /dev/null 2>&1; then mv ./bin/appsettings.*.json /app/config/; fi && \
    if ls ./bin/*.config 1> /dev/null 2>&1; then mv ./bin/*.config /app/config/; fi

# 设置最终权限（使用环境变量中的APP_UID如果存在）
RUN if [ -n "$APP_UID" ]; then \
        chown -R $APP_UID:0 /app && \
        chmod -R 775 /app; \
    else \
        chmod -R 755 /app; \
    fi

# 设置入口点
ENTRYPOINT ["dotnet", "/app/bin/WebProxy.dll"]