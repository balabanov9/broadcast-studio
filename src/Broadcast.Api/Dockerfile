FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Broadcast.Api/Broadcast.Api.csproj src/Broadcast.Api/
RUN dotnet restore src/Broadcast.Api/Broadcast.Api.csproj
COPY src/Broadcast.Api/ src/Broadcast.Api/
RUN dotnet publish src/Broadcast.Api -c Release --no-restore -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /out .
COPY graphics/ /app/graphics/
RUN mkdir -p /media /keys && chown -R app:app /media /keys
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "Broadcast.Api.dll"]
