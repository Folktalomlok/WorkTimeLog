# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/WorkTimeLog.Api/WorkTimeLog.Api.csproj src/WorkTimeLog.Api/
RUN dotnet restore src/WorkTimeLog.Api/WorkTimeLog.Api.csproj
COPY src/ src/
RUN dotnet publish src/WorkTimeLog.Api/WorkTimeLog.Api.csproj -c Release -o /app --no-restore

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# Render กำหนดพอร์ตผ่านตัวแปร PORT (แอปอ่านค่า "Port" จาก config อยู่แล้ว)
ENV PORT=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "WorkTimeLog.Api.dll"]
