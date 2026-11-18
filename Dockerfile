# ---------- Build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# restore ก่อน เพื่อให้ Docker cache layer นี้ไว้ (เร็วขึ้นเมื่อแก้แค่โค้ด)
COPY src/WorkTimeLog.Api/WorkTimeLog.Api.csproj src/WorkTimeLog.Api/
RUN dotnet restore src/WorkTimeLog.Api/WorkTimeLog.Api.csproj

COPY src/WorkTimeLog.Api/ src/WorkTimeLog.Api/
RUN dotnet publish src/WorkTimeLog.Api/WorkTimeLog.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- Runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# โฟลเดอร์ฐานข้อมูลที่ user "app" (non-root ของ image .NET 8) เขียนได้
RUN mkdir -p /app/data && chown -R app:app /app/data

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    DB_PATH=/app/data/worklog.db \
    PORT=8080

USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "WorkTimeLog.Api.dll"]
