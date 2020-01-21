using System;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using Unity.Burst;

/// <summary>
/// This system will run in the "default" world, which is different fromthe client
/// and server worlds. TBH not sure what the default world is/what role it plays.
/// 
/// This system will set up the connection between the client and the server. It
/// does so in the first frame of the game (after everything is created in game
/// startup and world initialization.)
/// 
/// So, this would be more aptly named "GameConnectionSetup"
/// </summary>
[UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
public class Game : ComponentSystem
{
    /// <summary>
    /// As you can see this is an empty component, used purely to identify an entity.
    /// 
    /// If you read on, you'll see this is used to make sure we only run the update
    /// for this system on the first frame by 
    ///     1) specifying that this component must xist as a singleton in the world 
    ///        for this system to update,
    ///     2) creating a singleton instance of this component at startup,
    ///     3) deleting it immediately on the first update so the update does not run again.
    /// </summary>
    struct InitGameComponent : IComponentData
    {
    }

    protected override void OnCreate()
    {
        // Specifies that there must be some InitGameComponent out there in the world
        // (possibly on another entity) for this particular system to run updates
        RequireSingletonForUpdate<InitGameComponent>();

        // Looks like this component system may be present outside of this scene, but we don't want that to happen...?
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "NetCube")
        {
            // Creates an entity with the required component so we can do the first update
            EntityManager.CreateEntity(typeof(InitGameComponent));
        }
    }

    protected override void OnUpdate()
    {
        // Destroy that singleton entity we required to update
        // We have effectively created a system which will do exactly one update
        // on the first frame of the game. We do it this way as we want this setup
        // logic to run after everything else is loaded and initialized; the only
        // way to guaruntee that order is to run on the first frame.
        EntityManager.DestroyEntity(GetSingletonEntity<InitGameComponent>());

        foreach (var world in World.AllWorlds)
        {
            var network = world.GetExistingSystem<NetworkStreamReceiveSystem>();

            // Check if we are on a client...
            if (world.GetExistingSystem<ClientSimulationSystemGroup>() != null)
            {
                // ...we are on a client! Set the address and port and connect!
                // Loopback = use localhost
                NetworkEndPoint ep = NetworkEndPoint.LoopbackIpv4;
                ep.Port = 7979;
                network.Connect(ep);
            }

            // Not sure why this is UNITY_EDITOR, but I'm pretty sure it's so the server
            // is only run in editor. This way you can only build clients and they don't
            // have to set up some way of differentiating client/server builds.
            #if UNITY_EDITOR 

            // Check if we are on the server...
            else if (world.GetExistingSystem<ServerSimulationSystemGroup>() != null)
            {
                // ...we are the server! Listen to clients from any ip address on
                // the specified port.
                NetworkEndPoint ep = NetworkEndPoint.AnyIpv4;
                ep.Port = 7979;
                network.Listen(ep);
            }

            #endif
        }
    }
}

/// <summary>
/// RPC request from a client to the server requesting that this client enter the game.
/// 
/// Note that this command only contains a dummy value to demonstrate how to read/write
/// on an rpc request. This particular struct could be totally empty, as it's presence
/// alone communicates that the requester wants to enter the game.
/// </summary>
[BurstCompile]
public struct GoInGameRequest : IRpcCommand
{
    // Unused integer to demonstrate how to write/read data on an rpc request
    public int value;

    public void Deserialize(DataStreamReader reader, ref DataStreamReader.Context ctx)
    {
        value = reader.ReadInt(ref ctx);
    }

    public void Serialize(DataStreamWriter writer)
    {
        writer.Write(value);
    }
    [BurstCompile]
    private static void InvokeExecute(ref RpcExecutor.Parameters parameters)
    {
        RpcExecutor.ExecuteCreateRequestComponent<GoInGameRequest>(ref parameters);
    }

    public PortableFunctionPointer<RpcExecutor.ExecuteDelegate> CompileExecute()
    {
        return new PortableFunctionPointer<RpcExecutor.ExecuteDelegate>(InvokeExecute);
    }
}

/// <summary>
/// Boilerplate system required so the requests can be handled by the engine.
/// The docs mention that this will eventually be automatically handled in the future.
/// </summary>
public class GoInGameRequestSystem : RpcCommandRequestSystem<GoInGameRequest>
{
}

/// <summary>
/// Runs on clients only. This system will send requests for all local networkd-id'd entities
/// on the client to be registered as connected with the server by sending the GoInGameRequest
/// command we created earlier.
/// </summary>
[UpdateInGroup(typeof(ClientSimulationSystemGroup))]
public class GoInGameClientSystem : ComponentSystem
{
    protected override void OnCreate()
    {
        // Honestly not sure how this component is created...
        RequireSingletonForUpdate<EnableNetCubeGhostReceiveSystemComponent>();
    }

    protected override void OnUpdate()
    {
        // Find all entities with a network id that don't yet have this NetworkStreamInGame component.
        // The component is used soley as an identifier that the entity is connected and in the game.
        // This is essentially boilerplate code we'll probably always want to have present.
        // See https://docs.unity3d.com/Packages/com.unity.netcode@0.0/manual/network-connection.html?q=NetworkStreamInGame
        Entities.WithNone<NetworkStreamInGame>().ForEach((Entity ent, ref NetworkIdComponent id) =>
        {
            // Mark that the entity is in game
            PostUpdateCommands.AddComponent<NetworkStreamInGame>(ent);

            // Create the actual request for this entity to be registered as connected on the server
            // Not sure why we do this after adding that NetworkStreamInGame - what if the command fails??
            var req = PostUpdateCommands.CreateEntity();
            PostUpdateCommands.AddComponent<GoInGameRequest>(req);
            PostUpdateCommands.AddComponent(req, new SendRpcCommandRequestComponent { TargetConnection = ent });
        });
    }
}

/// <summary>
/// Runs on servers only. This listens for new connections by finding all GoInGameRequest's
/// in the world. It will mark the entity as connected and create a new controllable cube.
/// It then removes those requests (as they are only used to indentify a new connection.)
/// </summary>
[UpdateInGroup(typeof(ServerSimulationSystemGroup))]
public class GoInGameServerSystem : ComponentSystem
{
    protected override void OnCreate()
    {
        // Honestly not sure how this component is created...
        RequireSingletonForUpdate<EnableNetCubeGhostSendSystemComponent>();
    }

    protected override void OnUpdate()
    {
        // Get all entities with the ReceiveRpc's component (and without the SendRpc's component),
        // and which also have a GoInGameRequest component indicating they wish to join the game.
        Entities.WithNone<SendRpcCommandRequestComponent>().ForEach((Entity reqEnt, ref GoInGameRequest req, ref ReceiveRpcCommandRequestComponent reqSrc) =>
        {
            // Mark that entity as in game
            PostUpdateCommands.AddComponent<NetworkStreamInGame>(reqSrc.SourceConnection);
            UnityEngine.Debug.Log(String.Format("Server setting connection {0} to in game", EntityManager.GetComponentData<NetworkIdComponent>(reqSrc.SourceConnection).Value));

            // Generate a controllable cube for this newly connected player with an input buffer
            // See CubeInput.cs
            var ghostCollection = GetSingleton<GhostPrefabCollectionComponent>();
            var ghostId = NetCubeGhostSerializerCollection.FindGhostType<CubeSnapshotData>();
            var prefab = EntityManager.GetBuffer<GhostPrefabBuffer>(ghostCollection.serverPrefabs)[ghostId].Value;
            var player = EntityManager.Instantiate(prefab);
            EntityManager.SetComponentData(player, new MovableCubeComponent { PlayerId = EntityManager.GetComponentData<NetworkIdComponent>(reqSrc.SourceConnection).Value});

            PostUpdateCommands.AddBuffer<CubeInput>(player);
            PostUpdateCommands.SetComponent(reqSrc.SourceConnection, new CommandTargetComponent {targetEntity = player});

            // We don't need that request any more, destroy it.
            PostUpdateCommands.DestroyEntity(reqEnt);
        });
    }
}
