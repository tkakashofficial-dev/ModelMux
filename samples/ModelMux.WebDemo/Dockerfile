# Build from the repository root so the referenced projects are in context:
#   docker build -f samples/ModelMux.WebDemo/Dockerfile -t modelmux-demo .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only project files first, so a source-only change doesn't invalidate the
# restore layer and re-download every package.
COPY Directory.Build.props ./
COPY src/ModelMux.Core/ModelMux.Core.csproj src/ModelMux.Core/
COPY src/ModelMux.Cost/ModelMux.Cost.csproj src/ModelMux.Cost/
COPY samples/ModelMux.WebDemo/ModelMux.WebDemo.csproj samples/ModelMux.WebDemo/
COPY README.md ./
RUN dotnet restore samples/ModelMux.WebDemo/ModelMux.WebDemo.csproj

COPY src/ src/
COPY samples/ModelMux.WebDemo/ samples/ModelMux.WebDemo/
RUN dotnet publish samples/ModelMux.WebDemo/ModelMux.WebDemo.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Run as a non-root user. The aspnet image ships one; there is no reason for a
# web demo to have root in its own container.
USER $APP_UID

COPY --from=build /app .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# API keys are NEVER baked into the image. Pass them at run time:
#   docker run -p 8080:8080 -e GEMINI_API_KEY=... -e OPENAI_API_KEY=... modelmux-demo
ENTRYPOINT ["dotnet", "ModelMux.WebDemo.dll"]
