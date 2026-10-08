namespace AffiVideo.Contracts;

public sealed record StatusResponse(DependencyStatusResponse Database, DependencyStatusResponse ObjectStorage);

public sealed record DependencyStatusResponse(bool Reachable);
