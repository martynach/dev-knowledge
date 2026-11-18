FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY backend/ backend/
COPY global.json ./
RUN dotnet restore backend/DevKnowledge.Api --locked-mode
RUN dotnet publish backend/DevKnowledge.Api -c Release --no-restore -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12
WORKDIR /app
COPY --from=build /out ./
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "DevKnowledge.Api.dll"]
