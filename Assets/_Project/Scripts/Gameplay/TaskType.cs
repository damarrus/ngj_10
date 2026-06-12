namespace Ngj10.Gameplay
{
    /// <summary>
    /// What an altar is currently asking for. The resource kinds mirror
    /// <see cref="ResourceType"/>; <see cref="Run"/> is the "run a route" quest that
    /// spawns zones on the map instead of consuming a resource.
    /// </summary>
    public enum TaskType
    {
        Sheep,
        Log,
        Berry,
        Run,
    }

    public static class TaskTypeExtensions
    {
        /// <summary>True for resource deliveries (handed in at the altar's zone).</summary>
        public static bool IsResource(this TaskType t) => t != TaskType.Run;

        /// <summary>Maps a resource task to its <see cref="ResourceType"/>. Invalid for Run.</summary>
        public static ResourceType ToResource(this TaskType t) => t switch
        {
            TaskType.Sheep => ResourceType.Sheep,
            TaskType.Log => ResourceType.Log,
            _ => ResourceType.Berry,
        };
    }
}
