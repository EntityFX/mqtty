namespace EntityFX.MqttY.Helper
{


    /// <summary>
    /// Calculates the best route between various paths, using Dijkstra's algorithm
    /// </summary>
    public static class DijkstraEngine
    {
        public static LinkedList<Path<T>> CalculateShortestPathBetween<T>(T source, T destination, IEnumerable<Path<T>> paths)
            where T : notnull
        {
            return CalculateFrom(source, paths)[destination];
        }

        public static Dictionary<T, LinkedList<Path<T>>> CalculateShortestFrom<T>(T source, IEnumerable<Path<T>> paths)
            where T : notnull
        {
            return CalculateFrom(source, paths);
        }

        private static Dictionary<T, LinkedList<Path<T>>> CalculateFrom<T>(T source, IEnumerable<Path<T>> paths)
            where T : notnull
        {
            // validate the paths
            if (paths.Any(p => p.Source.Equals(p.Destination) == true) == true)
                throw new ArgumentException("No path can have the same source and destination");

            // keep track of the shortest paths identified thus far
            Dictionary<T, KeyValuePair<int, LinkedList<Path<T>>>> shortestPaths = new Dictionary<T, KeyValuePair<int, LinkedList<Path<T>>>>();

            // keep track of the locations which have been completely processed
            List<T> locationsProcessed = new List<T>();

            // include all possible steps, with Int.MaxValue cost
            paths.SelectMany(p => new T[] { p.Source, p.Destination })           // union source and destinations
                    .Distinct()                                                  // remove duplicates
                    .ToList()                                                    // ToList exposes ForEach
                    .ForEach(s => shortestPaths.Set(s, Int32.MaxValue));   // add to ShortestPaths with MaxValue cost

            // update cost for self-to-self as 0; no path
            shortestPaths.Set(source, 0);

            // keep this cached
            var locationCount = shortestPaths.Keys.Count;

            while (locationsProcessed.Count < locationCount)
            {
                T? locationToProcess = default;

                //Search for the nearest location that isn't handled
                foreach (T location in shortestPaths.OrderBy(p => p.Value.Key).Select(p => p.Key).ToList())
                {
                    if (!locationsProcessed.Contains(location))
                    {
                        if (shortestPaths[location].Key == Int32.MaxValue)
                            return shortestPaths.ToDictionary(k => k.Key, v => v.Value.Value) 
                                ?? new Dictionary<T, LinkedList<Path<T>>>(); //ShortestPaths[destination].Value;

                        locationToProcess = location;
                        break;
                    }
                } // foreach

                var selectedPaths = paths.Where(p => p.Source.Equals(locationToProcess));

                foreach (Path<T> path in selectedPaths)
                {
                    if (shortestPaths[path.Destination].Key > path.Cost + shortestPaths[path.Source].Key)
                    {
                        shortestPaths.Set(
                            path.Destination,
                            path.Cost + shortestPaths[path.Source].Key,
                            shortestPaths[path.Source].Value.Union(new Path<T>[] { path }).ToArray());
                    }
                }

                if (locationToProcess == null)
                {
                    continue;
                }

                //Add the location to the list of processed locations
                locationsProcessed.Add(locationToProcess);
            } // while

            return shortestPaths.ToDictionary(k => k.Key, v => v.Value.Value);
            //return ShortestPaths[destination].Value;
        }
    }


}
