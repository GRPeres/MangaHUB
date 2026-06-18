FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/MangaHub.Web/MangaHub.Web.csproj -c Release -o /app/publish

FROM nginx:1.27-alpine
COPY src/MangaHub.Web/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

