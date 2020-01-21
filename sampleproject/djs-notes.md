Introduces different "worlds": https://docs.unity3d.com/Packages/com.unity.entities@0.0/manual/world.html
- Contains an EntityManager and set of ComponentSystems
  - Exactly one EntityManager per world!
- Common use: simulation World + presentation World.
- Networking: client World + server World

Some boilerplate code can be found in the NetCube Game.cs

Catered towards sending actions from client to server

## Ghosting (i.e. Replication) Objects (i.e. Replicated Objects)

### Ghost Objects (GhostAuthoringComponent)

From the video (11:55):
> "Network object owned by server, seen on client but you can't do anything with it because it doesn't exist" 

From [the docs](https://docs.unity3d.com/Packages/com.unity.netcode@0.0/manual/ghost-snapshots.html)
> A ghost is a networked object that the server simulates. During every frame, the server sends a snapshot of the current state of all ghosts to the client. The client presents them, but cannot directly control or affect them because the server owns them.

Use the "Ghost Authoring Component" to specify that a prefab should be replicated. You can specify which components (and which properties on a component) should be present on the client/server worlds. You can specify on a  per-property basis the importance (in case things need to be dropped for lag) as well as quality ("quantization") of values.

Note that the ghost should only be added on the **server world**. (I think, based on the video...)

Example: Physics Cube
- Rendering components only set for client
- Physics component only set for server


~~~
Can't use regular "Convert To Entity" scripts, as they convert to the "default" world, whereas for networking you need to use the client+server worlds. So, use "Convert To Client Server Entity" scripts (can send to client, server, or client+server worlds)
- Setting to server only results in the entities being present ONLY on the server
~~~

### Ghost Collections (GhostCollectionAuthoringComponent)

Indentifies all ghosts to be used in networking, and is used to spawn ghosts at runtime. This must be present on **both the client AND server worlds**!


## Input Networking

### `ICommandData<T>`

_See: `CubeInput.cs`_

## Replicating an object from Server → Client


### Case: Physics Cube

Creating a cube which will have physics simulations only run on the server (with no client-side prediction), and rendering only run on the clients:

- Add a "Ghost Collection Authoring Component" to the scene
- Make the object a prefab
- Add a "Ghost Authoring Component"
  - Allows you to choose which components on the prefab will be present on client vs server
  - Generates code indicating that this is some object to replicate & how to do so (based on that aforementioned config)
- Generate the code from the authoring component
- Add that prefab to the world, make sure it's under a "Convert To Client Server Entity" component set to only the server
- Refresh the ghost collection component


### Case: Player

Something that is "owned" by a player which the server can generate on connection and which is controlled by input events



- Add a "Ghost Collection Authoring Component" to the scene
- Create an `IComponentData` for the prefab:
  - with the `[GenerateAuthoringComponent]` attribute
  - and a `public int PlayerId;` with the `
    [GhostDefaultField]` attribute
  - _See: `MovableCubeComponent.cs`_
- Create an `ICommandData<T>` struct containing everything you need to represent player input
  - Contains fields as well as de/serialization logic
  - Must also have `CommandSendSystem<T>` and `CommandSendSystem<T>` classes defined for that input type; can be empty
- Create a `ComponentSystem` to handle sampling client input & generating the command data
  - On Update, get the entity 
