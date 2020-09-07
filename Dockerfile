FROM mcr.microsoft.com/dotnet/sdk:5.0 as build
COPY . /app/
WORKDIR /app
RUN dotnet restore
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:5.0 as runtime
WORKDIR /app
COPY --from=build /app/out/ ./
ENTRYPOINT ["dotnet", "Cecilifier.Web.dll"]