FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj files and restore dependencies
COPY ["PrintGest.Api/PrintGest.Api.csproj", "PrintGest.Api/"]
COPY ["PrintGest.Application/PrintGest.Application.csproj", "PrintGest.Application/"]
COPY ["PrintGest.Domain/PrintGest.Domain.csproj", "PrintGest.Domain/"]
COPY ["PrintGest.Infrastructure/PrintGest.Infrastructure.csproj", "PrintGest.Infrastructure/"]

RUN dotnet restore "PrintGest.Api/PrintGest.Api.csproj"

# Copy the rest of the source code
COPY . .
WORKDIR "/src/PrintGest.Api"
RUN dotnet build "PrintGest.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "PrintGest.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "PrintGest.Api.dll"]
