# Build with the SDK image, run on a chiseled (distroless) runtime image: no shell and no sops binary.
# The age key is not part of the image: mount it at /run/secrets/age/keys.txt.
FROM mcr.microsoft.com/dotnet/sdk:__DOTNET_VERSION__ AS build
WORKDIR /src
COPY . .
RUN dotnet publish NopsWorker.csproj -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:__DOTNET_VERSION__-noble-chiseled AS final
WORKDIR /app
COPY --from=build /out .
ENV SOPS_AGE_KEY_FILE=/run/secrets/age/keys.txt
ENTRYPOINT ["dotnet", "NopsWorker.dll"]
