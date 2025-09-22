# 请参阅 https://aka.ms/customizecontainer 以了解如何自定义调试容器，以及 Visual Studio 如何使用此 Dockerfile 生成映像以更快地进行调试。

# 此阶段用于在快速模式(默认为调试配置)下从 VS 运行时
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 7080
ENV Platform=Docker
ENV CONFIG_PATH=/config

# 设置容器的本地化，确保 UTF-8 支持
ENV LANG=C.UTF-8
ENV LC_ALL=C.UTF-8

#设置时间为中国上海
ENV TZ=Asia/Shanghai

# 此阶段用于生成服务项目
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["WebProxy.csproj", "."]
RUN dotnet restore "./WebProxy.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "./WebProxy.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 此阶段用于发布要复制到最终阶段的服务项目
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./WebProxy.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# 此阶段在生产中使用，或在常规模式下从 VS 运行时使用(在不使用调试配置时为默认值)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# 仅容器需要的额外设置
RUN mkdir /config /certs \
    && chown -R $APP_UID:$APP_UID /config /certs \
    && mv /app/appsettings*.json /config/ || true

ENTRYPOINT ["dotnet", "WebProxy.dll"]