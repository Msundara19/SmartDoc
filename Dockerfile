FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY SmartDoc.Api/SmartDoc.Api.csproj SmartDoc.Api/
RUN dotnet restore SmartDoc.Api/SmartDoc.Api.csproj
COPY SmartDoc.Api/ SmartDoc.Api/
RUN dotnet publish SmartDoc.Api/SmartDoc.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "SmartDoc.Api.dll"]
