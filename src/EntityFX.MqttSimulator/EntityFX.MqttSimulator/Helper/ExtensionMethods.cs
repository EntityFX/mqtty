namespace EntityFX.MqttY.Helper
{
    using System.Collections.Generic;
    using EntityFX.MqttY.PathFinder;

    public static class ExtensionMethods
    {
        /// <summary>
        /// Adds or Updates the dictionary to include the destination and its associated cost 
        /// and complete path (and param arrays make paths easier to work with)
        /// </summary>
        public static void Set<T>(this Dictionary<T, KeyValuePair<int, LinkedList<Path<T>>>> dictionary,
                                  T destination, int cost, params Path<T>[] paths)
            where T: notnull
        {
            var completePath = paths == null ? new LinkedList<Path<T>>() : new LinkedList<Path<T>>(paths);
            dictionary[destination] = new KeyValuePair<int, LinkedList<Path<T>>>(cost, completePath);
        }
    }
}
