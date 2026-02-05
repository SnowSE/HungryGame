#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["clients/foolhearty/foolhearty.csproj", "clients/foolhearty/"]
RUN dotnet restore "clients/foolhearty/foolhearty.csproj"
COPY . .
WORKDIR "/src/clients/foolhearty"
RUN dotnet build "foolhearty.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "foolhearty.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "foolhearty.dll"]