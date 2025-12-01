namespace EntityFX.MqttY.PathFinder
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    public class DijkstraWeightedGraph<TNode>
    {
        private Dictionary<int, List<(int vertex, byte weight)>> _adjacencyList = new();
        private Dictionary<int, TNode> _nodes = new();

        public void AddEdge(int from, TNode fromNode, int to, TNode toNode, byte weight)
        {
            if (!_adjacencyList.ContainsKey(from))
                _adjacencyList[from] = new List<(int, byte)>();
            if (!_adjacencyList.ContainsKey(to))
                _adjacencyList[to] = new List<(int, byte)>();

            _adjacencyList[from].Add((to, weight));
            _adjacencyList[to].Add((from, weight));
            _nodes[from] = fromNode;
            _nodes[to] = toNode;
        }

        public List<(int vertex, TNode item)> FindShortestPath(int start, int end)
        {
            if (start == end)
            {
                var node = _adjacencyList[start];
                return new List<(int vertex, TNode item)> {
                    (start, _nodes[start])
                };
            }

            if (!_adjacencyList.ContainsKey(start) || !_adjacencyList.ContainsKey(end))
                return new List<(int vertex, TNode item)>();

            var distances = new Dictionary<int, int>();
            var previous = new Dictionary<int, int>();
            var visited = new HashSet<int>();

            var priorityQueue = new PriorityQueue<int, int>();

            foreach (var vertex in _adjacencyList.Keys)
            {
                distances[vertex] = vertex == start ? 0 : int.MaxValue;
            }

            priorityQueue.Enqueue(start, 0);
            previous[start] = -1;

            while (priorityQueue.Count > 0)
            {
                var currentVertex = priorityQueue.Dequeue();

                if (currentVertex == end)
                    break;

                if (visited.Contains(currentVertex))
                    continue;

                visited.Add(currentVertex);

                foreach (var (neighbor, weight) in _adjacencyList[currentVertex])
                {
                    if (visited.Contains(neighbor))
                        continue;

                    var newDistance = distances[currentVertex] + weight;

                    if (newDistance < distances[neighbor])
                    {
                        distances[neighbor] = newDistance;
                        previous[neighbor] = currentVertex;
                        priorityQueue.Enqueue(neighbor, newDistance);
                    }
                }
            }

            return previous.ContainsKey(end) ? ReconstructPath(previous!, end) : new List<(int vertex, TNode item)>();
        }

        public void Clear()
        {
            _adjacencyList.Clear();
        }

        private List<(int vertex, TNode item)> ReconstructPath(
            Dictionary<int, int> previous, int end)
        {
            var path = new List<(int vertex, TNode item)>();
            for (int vertex = end; vertex != -1; vertex = previous[vertex])
            {
                path.Add((vertex, _nodes[vertex]));
            }

            path.Reverse();
            return path;
        }
    }
}
