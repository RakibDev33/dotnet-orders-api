# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/Orders.Api/Orders.Api.csproj src/Orders.Api/
RUN dotnet restore src/Orders.Api/Orders.Api.csproj
COPY src/ src/
RUN dotnet publish src/Orders.Api/Orders.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN mkdir -p /app/data && chown "$APP_UID" /app/data
COPY --from=build /app .
ENV ConnectionStrings__Orders="Data Source=/app/data/orders.db"
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Orders.Api.dll"]
