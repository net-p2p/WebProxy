# 请参阅 https://aka.ms/customizecontainer 以了解如何自定义调试容器，以及 Visual Studio 如何使用此 Dockerfile 生成映像以更快地进行调试。

# 第一阶段：基础运行环境（用于调试和生产）
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base

# 设置默认的用户ID（如果没有从外部传入）
ARG APP_UID=1000
USER $APP_UID

# 设置工作目录
WORKDIR /app

# 暴露应用程序端口
EXPOSE 7080

# 设置环境变量
ENV Platform=Docker
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8
ENV TZ=Asia/Shanghai

# 创建所需的目录结构（使用明确的用户ID）
RUN adduser -u ${APP_UID} --disabled-password --gecos "" appuser || true && \
    mkdir -p /app/bin /app/config /app/certs /app/logs /app/temp && \
    chown -R ${APP_UID}:${APP_UID} /app && \
    chmod -R 755 /app

# 第二阶段：构建应用程序
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# 复制项目文件并恢复依赖
COPY ["WebProxy.csproj", "."]
RUN dotnet restore "./WebProxy.csproj"

# 复制所有源代码
COPY . .

# 构建应用程序（添加DOCKER编译常量）
RUN dotnet build "./WebProxy.csproj" -c $BUILD_CONFIGURATION -o /app/build /p:DefineConstants="DOCKER" /p:Configuration=Docker

# 第三阶段：发布应用程序
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./WebProxy.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false /p:DefineConstants="DOCKER" /p:Configuration=Docker

# 第四阶段：最终生产镜像
FROM base AS final
WORKDIR /app

# 从发布阶段复制构建结果到 bin 目录
COPY --from=publish /app/publish ./bin/

# 清理不必要的文件
RUN find /app/bin -name "*.pdb" -delete \
    && find /app/bin -name "*.xml" -delete \
    && find /app/bin -name "*.config" -delete \
    && rm -rf /app/bin/runtimes || true

# 创建配置目录并将配置文件移动到指定位置
RUN mkdir -p /app/config \
    && mv /app/bin/appsettings*.json /app/config/ 2>/dev/null || true \
    && mv /app/bin/*.config /app/config/ 2>/dev/null || true

# 设置目录权限（使用明确的用户ID）
ARG APP_UID=1000
RUN chown -R $APP_UID:$APP_UID /app \
    && chmod -R 755 /app

# 设置入口点，指定从 bin 目录启动
ENTRYPOINT ["dotnet", "/app/bin/WebProxy.dll"]