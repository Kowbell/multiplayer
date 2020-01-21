/// <summary>
/// https://docs.unity3d.com/Packages/com.unity.netcode@0.0/manual/command-stream.html
///
/// Here we define a networkable representation of the player's input, as well as a
/// client-side system which will generate those input data structures each frame
/// by sampling the current input using the standard engine input system.
/// 
/// See MoveCubeSystem.cs for the server-side system that will process this input data.
/// </summary>

using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

// I think this is code that will only be present in game.cs, also stored here for the presentation...
// #if SERVER_INPUT_SETUP
//             var ghostCollection = GetSingleton<GhostPrefabCollectionComponent>();
//             var ghostId = GhostSerializerCollection.FindGhostType<CubeSnapshotData>();
//             var prefab = EntityManager.GetBuffer<GhostPrefabBuffer>(ghostCollection.serverPrefabs)[ghostId].Value;
//             var player = EntityManager.Instantiate(prefab);
//             EntityManager.SetComponentData(player, new MovableCubeComponent { PlayerId = EntityManager.GetComponentData<NetworkIdComponent>(req.SourceConnection).Value});

//             PostUpdateCommands.AddBuffer<CubeInput>(player);
//             PostUpdateCommands.SetComponent(req.SourceConnection, new CommandTargetComponent {targetEntity = player});
// #endif

/// <summary>
/// Contains all the data representing player input for the cube, as well as de/serialization logic
/// </summary>
public struct CubeInput : ICommandData<CubeInput>
{
    public uint Tick => tick;
    public uint tick;
    public int horizontal;
    public int vertical;

    public void Deserialize(uint tick, DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
        this.tick = tick;
        horizontal = reader.ReadInt(ref ctx);
        vertical = reader.ReadInt(ref ctx);
    }

    public void Serialize(DataStreamWriter writer)
    {
        writer.Write(horizontal);
        writer.Write(vertical);
    }

    public void Deserialize(uint tick, DataStreamReader reader, ref DataStreamReader.Context ctx, CubeInput baseline,
        NetworkCompressionModel compressionModel)
    {
        Deserialize(tick, reader, ref ctx);
    }

    public void Serialize(DataStreamWriter writer, CubeInput baseline, NetworkCompressionModel compressionModel)
    {
        Serialize(writer);
    }
}

/// <summary> Boilerplate system for networking input </summary>
public class NetCubeSendCommandSystem : CommandSendSystem<CubeInput>
{
}

/// <summary> Boilerplate system for networking input </summary>
public class NetCubeReceiveCommandSystem : CommandReceiveSystem<CubeInput>
{
}


/// <summary>
/// Handles taking the local player's input (using the standard engine input system) and writing that to CubeInput
/// command data structures that are then buffered and eventually sent to and processed by the server (see MoveCubeSystem.cs)
/// 
/// Note that this runs on the client only.
/// </summary>
[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public class SampleCubeInput : ComponentSystem
{
    protected override void OnCreate()
    {
        RequireSingletonForUpdate<NetworkIdComponent>();
        RequireSingletonForUpdate<EnableNetCubeGhostReceiveSystemComponent>();
    }

    protected override void OnUpdate()
    {
        // Get entity which we control
        // This entity will be referenced by the singleton `CommandTargetComponent`
        // If there isn't one present present, we'll set one up.
        var localInput = GetSingleton<CommandTargetComponent>().targetEntity;
        if (localInput == Entity.Null)
        {
            // The NetworkIdComponent simply stores the local client's networking id
            var localPlayerId = GetSingleton<NetworkIdComponent>().Value;

            // Get all entities with a MovableCubeComponent and with no CubeInput buffer
            Entities.WithNone<CubeInput>().ForEach((Entity ent, ref MovableCubeComponent cube) =>
            {
                if (cube.PlayerId == localPlayerId)
                {
                    // This is our cube, set it up with an input buffer
                    PostUpdateCommands.AddBuffer<CubeInput>(ent);
                    // Register that this is our cube which we control that that global target component
                    PostUpdateCommands.SetComponent(GetSingletonEntity<CommandTargetComponent>(), new CommandTargetComponent {targetEntity = ent});
                }
            });
            
            // We didn't have a cube to control this frame, so we can't really do anything with input
            // Wait 'til next frame to see if we had found one and set it in that above loop
            return;
        }

        // We now have our controlled cube entity!

        // Set up our arbitrary input data
        var input = default(CubeInput);
        input.tick = World.GetExistingSystem<ClientSimulationSystemGroup>().ServerTick;
        if (Input.GetKey("a"))
            input.horizontal -= 1;
        if (Input.GetKey("d"))
            input.horizontal += 1;
        if (Input.GetKey("s"))
            input.vertical -= 1;
        if (Input.GetKey("w"))
            input.vertical += 1;

        // Get that input buffer on our controlled cube entity
        var inputBuffer = EntityManager.GetBuffer<CubeInput>(localInput);

        // Add our input to that buffer
        // This will be processed in the MoveCubeSystem
        inputBuffer.AddCommandData(input);
    }
}