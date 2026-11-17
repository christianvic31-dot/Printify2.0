FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Backend/PrintifyBackend.csproj Backend/
RUN dotnet restore Backend/PrintifyBackend.csproj

COPY Backend/ Backend/
COPY Frontend/ Frontend/
RUN dotnet publish Backend/PrintifyBackend.csproj --configuration Release --output /out/Backend --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app/Backend

COPY --from=build /out/Backend/ ./
COPY --from=build /src/Frontend/ /app/Frontend/

USER $APP_UID
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "PrintifyBackend.dll"]
