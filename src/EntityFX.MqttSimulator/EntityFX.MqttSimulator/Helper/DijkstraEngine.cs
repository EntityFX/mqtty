﻿using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EntityFX.MqttY.Helper
{


    /// <summary>
    /// Calculates the best route between various paths, using Dijkstra's algorithm
    /// </summary>
    public static class DijkstraEngine
    {
        public static LinkedList<Path<T>> CalculateShortestPathBetween<T>(T source, T destination, IEnumerable<Path<T>> Paths)
            where T : notnull
        {
            return CalculateFrom(source, Paths)[destination];
        }

        public static Dictionary<T, LinkedList<Path<T>>> CalculateShortestFrom<T>(T source, IEnumerable<Path<T>> Paths)
            where T : notnull
        {
            return CalculateFrom(source, Paths);
        }

        private static Dictionary<T, LinkedList<Path<T>>> CalculateFrom<T>(T source, IEnumerable<Path<T>> Paths)
            where T : notnull
        {
            // validate the paths
            if (Paths.Any(p => p.Source.Equals(p.Destination) == true) == true)
                throw new ArgumentException("No path can have the same source and destination");

            // keep track of the shortest paths identified thus far
            Dictionary<T, KeyValuePair<int, LinkedList<Path<T>>>> ShortestPaths = new Dictionary<T, KeyValuePair<int, LinkedList<Path<T>>>>();

            // keep track of the locations which have been completely processed
            List<T> LocationsProcessed = new List<T>();

            // include all possible steps, with Int.MaxValue cost
            Paths.SelectMany(p => new T[] { p.Source, p.Destination })           // union source and destinations
                    .Distinct()                                                  // remove duplicates
                    .ToList()                                                    // ToList exposes ForEach
                    .ForEach(s => ShortestPaths.Set(s, Int32.MaxValue));   // add to ShortestPaths with MaxValue cost

            // update cost for self-to-self as 0; no path
            ShortestPaths.Set(source, 0);

            // keep this cached
            var LocationCount = ShortestPaths.Keys.Count;

            while (LocationsProcessed.Count < LocationCount)
            {
                T? _locationToProcess = default;

                //Search for the nearest location that isn't handled
                foreach (T _location in ShortestPaths.OrderBy(p => p.Value.Key).Select(p => p.Key).ToList())
                {
                    if (!LocationsProcessed.Contains(_location))
                    {
                        if (ShortestPaths[_location].Key == Int32.MaxValue)
                            return ShortestPaths.ToDictionary(k => k.Key, v => v.Value.Value) 
                                ?? new Dictionary<T, LinkedList<Path<T>>>(); //ShortestPaths[destination].Value;

                        _locationToProcess = _location;
                        break;
                    }
                } // foreach

                var _selectedPaths = Paths.Where(p => p.Source.Equals(_locationToProcess));

                foreach (Path<T> path in _selectedPaths)
                {
                    if (ShortestPaths[path.Destination].Key > path.Cost + ShortestPaths[path.Source].Key)
                    {
                        ShortestPaths.Set(
                            path.Destination,
                            path.Cost + ShortestPaths[path.Source].Key,
                            ShortestPaths[path.Source].Value.Union(new Path<T>[] { path }).ToArray());
                    }
                }

                if (_locationToProcess == null)
                {
                    continue;
                }

                //Add the location to the list of processed locations
                LocationsProcessed.Add(_locationToProcess);
            } // while

            return ShortestPaths.ToDictionary(k => k.Key, v => v.Value.Value);
            //return ShortestPaths[destination].Value;
        }
    }


}
