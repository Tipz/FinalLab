FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS build
ARG BUILDARCH
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && case "$BUILDARCH" in \
         amd64) sdk_arch=x64 ;; \
         arm64) sdk_arch=arm64 ;; \
         *) echo "Unsupported build architecture: $BUILDARCH" >&2; exit 1 ;; \
       esac \
    && sdk_archive="dotnet-sdk-10.0.303-linux-${sdk_arch}.tar.gz" \
    && cd /tmp \
    && curl --fail --show-error --location --remote-name \
       "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.303/${sdk_archive}" \
    && curl --fail --show-error --location --remote-name \
       "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.303/${sdk_archive}.sha512" \
    && sha512sum --check "${sdk_archive}.sha512" \
    && tar --gzip --extract --file "$sdk_archive" --directory /usr/share/dotnet \
    && rm "$sdk_archive" "${sdk_archive}.sha512" \
    && test "$(dotnet --version)" = "10.0.303"
WORKDIR /src

COPY global.json Directory.Build.props Together.slnx ./
COPY .config .config
COPY src/Together.Core/Together.Core.csproj src/Together.Core/packages.lock.json src/Together.Core/
COPY src/Together.Contracts/Together.Contracts.csproj src/Together.Contracts/packages.lock.json src/Together.Contracts/
COPY src/Together.Client/Together.Client.csproj src/Together.Client/packages.lock.json src/Together.Client/
COPY src/Together.Api/Together.Api.csproj src/Together.Api/packages.lock.json src/Together.Api/
RUN dotnet restore src/Together.Api/Together.Api.csproj --locked-mode \
    && dotnet restore src/Together.Client/Together.Client.csproj --locked-mode

COPY src src
RUN dotnet publish src/Together.Client/Together.Client.csproj -c Release --no-restore -o /out/client \
    && dotnet publish src/Together.Api/Together.Api.csproj -c Release --no-restore -o /out/api \
    && cp -R /out/client/wwwroot/. /out/api/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS final
WORKDIR /app
COPY --from=build --chown=app:app /out/api .
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data-protection \
    && chown -R app:app /app/data-protection
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER app
HEALTHCHECK --interval=10s --timeout=5s --start-period=15s --retries=6 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "Together.Api.dll"]
