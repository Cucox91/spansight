namespace SpanSight.Api.Tests.Integration;

/// <summary>
/// Integration tests need a container runtime (Testcontainers → real PostGIS). On machines
/// without Docker the suite skips instead of failing — CI and the dev Mac run it for real.
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!IsDockerAvailable())
        {
            Skip = "Docker is not available on this machine; integration tests run where it is (dev Mac / CI).";
        }
    }

    private static bool IsDockerAvailable() =>
        Environment.GetEnvironmentVariable("DOCKER_HOST") is not null
        || File.Exists("/var/run/docker.sock")
        || OperatingSystem.IsWindows();
}
