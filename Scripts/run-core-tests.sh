#!/usr/bin/env bash
# Runs the pure C# VRMCast.Core tests with the .NET SDK, without Unity.
#
# The same test sources run inside Unity's Test Runner (EditMode). This script exists so the
# engine-free logic (VRM inspection, framing math, backgrounds, diagnostics) can be verified in CI
# or on a machine without Unity installed. Requires: dotnet SDK 8.x.
#
# Usage: Scripts/run-core-tests.sh [extra dotnet test args]
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WORK_DIR="${VRMCAST_TEST_WORKDIR:-$REPO_ROOT/Temp/CoreTests}"
CORE_DIR="$REPO_ROOT/Assets/VRMCast/Core"
TESTS_DIR="$REPO_ROOT/Assets/VRMCast/Tests/EditMode"

rm -rf "$WORK_DIR"
mkdir -p "$WORK_DIR/Core" "$WORK_DIR/Tests"

# Unity 6 compiles C# 9 against the .NET Standard 2.1 profile; mirror that here.
cat > "$WORK_DIR/Core/VRMCast.Core.csproj" <<CSPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RootNamespace>VRMCast.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$CORE_DIR/**/*.cs" />
  </ItemGroup>
</Project>
CSPROJ

cat > "$WORK_DIR/Tests/VRMCast.Core.Tests.csproj" <<CSPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <RootNamespace>VRMCast.Core.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$TESTS_DIR/**/*.cs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="NUnit" Version="3.14.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.6.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Core/VRMCast.Core.csproj" />
  </ItemGroup>
</Project>
CSPROJ

cd "$WORK_DIR/Tests"
dotnet test --nologo -v minimal "$@"
