using Unity.Entities;
using Unity.NetCode;

/// <summary>
/// Simple indentification component whose presence indicates an object we will
/// be moving about the game world.
/// </summary>
[GenerateAuthoringComponent]
public struct MovableCubeComponent : IComponentData
{
    [GhostDefaultField]
    public int PlayerId;
}
