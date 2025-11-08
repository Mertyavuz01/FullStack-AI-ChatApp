# .NET 8 SDK ile build et
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Proje dosyasını kopyala
COPY ["ChatBackend.csproj", "./"]
RUN dotnet restore "ChatBackend.csproj"

# Tüm dosyaları kopyala ve derle
COPY . .
RUN dotnet publish "ChatBackend.csproj" -c Release -o /app/publish

# Runtime imajı
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 10000
ENV ASPNETCORE_URLS=http://+:10000
ENTRYPOINT ["dotnet", "ChatBackend.dll"]
