#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
RUN apt-get update && apt-get install -y curl
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["HungryGame/HungryGame.csproj", "HungryGame/"]
RUN dotnet restore "HungryGame/HungryGame.csproj"
COPY . .
WORKDIR "/src/HungryGame"
RUN dotnet build "HungryGame.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HungryGame.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HungryGame.dll"]