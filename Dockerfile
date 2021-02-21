FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine3.13-amd64 as build
COPY . /app/
WORKDIR /app
RUN dotnet restore
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine3.13-amd64 as runtime
WORKDIR /app
COPY --from=build /app/out/ ./
ENTRYPOINT ["dotnet", "Cecilifier.Web.dll"]

#sudo docker build . -t cecilifier/6.0
#sudo docker run -d -p 8081:8081 cecilifier/6.0 (can't get it to work on Linux Mint)
#sudo docker run --net host -d cecilifier/6.0