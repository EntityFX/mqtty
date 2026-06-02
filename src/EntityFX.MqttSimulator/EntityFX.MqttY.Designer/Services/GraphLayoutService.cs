using Avalonia;
using EntityFX.MqttY.Designer.Models;

namespace EntityFX.MqttY.Designer.Services;

public class GraphLayoutService : IGraphLayoutService
{
    // Reference sizes for layout calculations
    private static readonly Size NetworkSize = new(140, 70);
    private static readonly Size NodeSize = new(120, 60);
    private const double NetworkNodeSpacing = 40;
    private const double InterNetworkSpacing = 60;

    public GraphLayoutModel CalculateLayout(NetworkDesignModel model)
    {
        var layout = new GraphLayoutModel();
        var networks = model.Networks.ToList();
        var nodes = model.Nodes.ToList();

        // Simple grid layout for networks
        int cols = (int)Math.Ceiling(Math.Sqrt(networks.Count));
        for (int i = 0; i < networks.Count; i++)
        {
            int row = i / cols;
            int col = i % cols;
            layout.NetworkPositions[networks[i].Name] = new Point(
                100 + col * 250,
                100 + row * 200);
        }

        // Place nodes near their network
        foreach (var node in nodes)
        {
            if (node.Network != null && layout.NetworkPositions.TryGetValue(node.Network, out var netPos))
            {
                // Offset from network position
                var offset = new Point(
                    netPos.X + 150 + (layout.NodePositions.Count % 3) * 100,
                    netPos.Y + 80 + (layout.NodePositions.Count / 3) * 60);
                layout.NodePositions[node.Name] = offset;
            }
            else
            {
                layout.NodePositions[node.Name] = new Point(400, 400);
            }
        }

        return layout;
    }

    public GraphLayoutModel ApplyForceDirected(NetworkDesignModel model, GraphLayoutModel current)
    {
        // Simple force-directed layout improvement
        // For MVP, just return current layout
        return current;
    }

    public GraphLayoutModel CalculateCircularLayout(NetworkDesignModel model)
    {
        var layout = new GraphLayoutModel();
        var networks = model.Networks.ToList();
        var nodes = model.Nodes.ToList();

        if (networks.Count == 0 && nodes.Count == 0)
            return layout;

        double centerX = 400;
        double centerY = 300;
        double networkRadius = Math.Max(200, networks.Count * 90);

        // Place networks in a circle
        for (int i = 0; i < networks.Count; i++)
        {
            double angle = 2 * Math.PI * i / networks.Count - Math.PI / 2;
            layout.NetworkPositions[networks[i].Name] = new Point(
                centerX + networkRadius * Math.Cos(angle) - NetworkSize.Width / 2,
                centerY + networkRadius * Math.Sin(angle) - NetworkSize.Height / 2);
        }

        // Place nodes in smaller circles around their network
        foreach (var node in nodes)
        {
            if (node.Network != null && layout.NetworkPositions.TryGetValue(node.Network, out var netPos))
            {
                var siblings = nodes.Where(n => n.Network == node.Network).ToList();
                var idx = siblings.IndexOf(node);
                int siblingCount = siblings.Count;
                double nodeRadius = 110;
                double nodeAngle = 2 * Math.PI * idx / Math.Max(1, siblingCount) - Math.PI / 2;

                layout.NodePositions[node.Name] = new Point(
                    netPos.X + NetworkSize.Width / 2 + nodeRadius * Math.Cos(nodeAngle) - NodeSize.Width / 2,
                    netPos.Y + NetworkSize.Height / 2 + nodeRadius * Math.Sin(nodeAngle) - NodeSize.Height / 2);
            }
            else
            {
                layout.NodePositions[node.Name] = new Point(centerX + 250, centerY);
            }
        }

        return layout;
    }

    /// <summary>
    /// Force-Directed (gravitational) layout algorithm.
    /// Finds the most connected node as center, then iteratively applies
    /// repulsive forces (Coulomb) between all pairs and attractive forces
    /// (spring) along edges. Accounts for node sizes.
    /// </summary>
    public GraphLayoutModel CalculateForceDirectedLayout(NetworkDesignModel model)
    {
        var layout = new GraphLayoutModel();
        var networks = model.Networks.ToList();
        var nodes = model.Nodes.ToList();

        if (networks.Count == 0 && nodes.Count == 0)
            return layout;

        // Build a list of all elements (networks + nodes) with their sizes
        var elements = new List<ForceElement>();
        var elementByName = new Dictionary<string, ForceElement>();

        foreach (var net in networks)
        {
            var el = new ForceElement
            {
                Name = net.Name,
                Width = NetworkSize.Width,
                Height = NetworkSize.Height,
                IsNetwork = true,
                Position = new Point(0, 0)
            };
            elements.Add(el);
            elementByName[net.Name] = el;
        }

        foreach (var node in nodes)
        {
            var el = new ForceElement
            {
                Name = node.Name,
                Width = NodeSize.Width,
                Height = NodeSize.Height,
                IsNetwork = false,
                Network = node.Network,
                Position = new Point(0, 0)
            };
            elements.Add(el);
            elementByName[node.Name] = el;
        }

        // Build adjacency list (edges)
        var edges = new List<(ForceElement From, ForceElement To)>();
        foreach (var net in networks)
        {
            if (!elementByName.TryGetValue(net.Name, out var fromEl)) continue;
            foreach (var link in net.Links)
            {
                if (link.TargetNetwork != null && elementByName.TryGetValue(link.TargetNetwork, out var toEl))
                {
                    edges.Add((fromEl, toEl));
                }
            }
        }
        foreach (var node in nodes)
        {
            if (node.Network == null) continue;
            if (!elementByName.TryGetValue(node.Name, out var nodeEl)) continue;
            if (!elementByName.TryGetValue(node.Network, out var netEl)) continue;
            edges.Add((nodeEl, netEl));
        }

        // Find the most connected element (center)
        var connectionCount = new Dictionary<string, int>();
        foreach (var edge in edges)
        {
            connectionCount[edge.From.Name] = connectionCount.GetValueOrDefault(edge.From.Name) + 1;
            connectionCount[edge.To.Name] = connectionCount.GetValueOrDefault(edge.To.Name) + 1;
        }
        var centerName = connectionCount.OrderByDescending(kv => kv.Value).FirstOrDefault().Key;
        var centerEl = centerName != null ? elementByName.GetValueOrDefault(centerName) : elements.FirstOrDefault();

        // Initialize positions: center at origin, others scattered around
        double centerX = 400;
        double centerY = 300;
        var rng = new Random(42);
        foreach (var el in elements)
        {
            if (el == centerEl)
            {
                el.Position = new Point(centerX, centerY);
            }
            else
            {
                double angle = rng.NextDouble() * 2 * Math.PI;
                double dist = 150 + rng.NextDouble() * 200;
                el.Position = new Point(
                    centerX + dist * Math.Cos(angle),
                    centerY + dist * Math.Sin(angle));
            }
        }

        // Force-directed parameters
        double repulsionStrength = 50000;
        double attractionStrength = 0.01;
        double damping = 0.85;
        double minDistance = 30;
        int iterations = 100;

        // Iterative force simulation
        for (int iter = 0; iter < iterations; iter++)
        {
            var forces = new Dictionary<string, Vector>();

            foreach (var el in elements)
                forces[el.Name] = new Vector(0, 0);

            // Repulsive forces (Coulomb): all pairs repel
            for (int i = 0; i < elements.Count; i++)
            {
                for (int j = i + 1; j < elements.Count; j++)
                {
                    var a = elements[i];
                    var b = elements[j];
                    var delta = new Vector(
                        a.Position.X - b.Position.X,
                        a.Position.Y - b.Position.Y);
                    double dist = Math.Max(Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y), minDistance);

                    // Size-aware repulsion: bigger elements repel more
                    double sizeFactor = (a.Width * a.Height + b.Width * b.Height) / (NodeSize.Width * NodeSize.Height);
                    double force = repulsionStrength * sizeFactor / (dist * dist);

                    double fx = force * delta.X / dist;
                    double fy = force * delta.Y / dist;

                    forces[a.Name] = new Vector(forces[a.Name].X + fx, forces[a.Name].Y + fy);
                    forces[b.Name] = new Vector(forces[b.Name].X - fx, forces[b.Name].Y - fy);
                }
            }

            // Attractive forces (spring): connected elements attract
            foreach (var edge in edges)
            {
                var a = edge.From;
                var b = edge.To;
                var delta = new Vector(
                    b.Position.X - a.Position.X,
                    b.Position.Y - a.Position.Y);
                double dist = Math.Max(Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y), minDistance);

                double force = attractionStrength * dist;
                double fx = force * delta.X / dist;
                double fy = force * delta.Y / dist;

                forces[a.Name] = new Vector(forces[a.Name].X + fx, forces[a.Name].Y + fy);
                forces[b.Name] = new Vector(forces[b.Name].X - fx, forces[b.Name].Y - fy);
            }

            // Center gravity: pull everything gently toward center
            foreach (var el in elements)
            {
                if (el == centerEl) continue;
                var toCenter = new Vector(centerX - el.Position.X, centerY - el.Position.Y);
                double distToCenter = Math.Sqrt(toCenter.X * toCenter.X + toCenter.Y * toCenter.Y);
                if (distToCenter > 10)
                {
                    double gravity = 0.001 * distToCenter;
                    forces[el.Name] = new Vector(
                        forces[el.Name].X + gravity * toCenter.X / distToCenter,
                        forces[el.Name].Y + gravity * toCenter.Y / distToCenter);
                }
            }

            // Apply forces with damping
            foreach (var el in elements)
            {
                var f = forces[el.Name];
                el.Position = new Point(
                    el.Position.X + f.X * damping,
                    el.Position.Y + f.Y * damping);
            }
        }

        // Clamp positions to visible area
        foreach (var el in elements)
        {
            el.Position = new Point(
                Math.Max(20, Math.Min(el.Position.X, 2000)),
                Math.Max(20, Math.Min(el.Position.Y, 2000)));
        }

        // Transfer positions back to layout
        foreach (var net in networks)
        {
            if (elementByName.TryGetValue(net.Name, out var el))
                layout.NetworkPositions[net.Name] = el.Position;
        }
        foreach (var node in nodes)
        {
            if (elementByName.TryGetValue(node.Name, out var el))
                layout.NodePositions[node.Name] = el.Position;
        }

        return layout;
    }

    /// <summary>
    /// Grid layout algorithm. Places all elements in a grid pattern
    /// with cell size based on element dimensions.
    /// Networks in first rows, nodes under their networks.
    /// </summary>
    public GraphLayoutModel CalculateGridLayout(NetworkDesignModel model)
    {
        var layout = new GraphLayoutModel();
        var networks = model.Networks.ToList();
        var nodes = model.Nodes.ToList();

        if (networks.Count == 0 && nodes.Count == 0)
            return layout;

        // Calculate cell size based on max element dimensions + padding
        double cellWidth = Math.Max(NetworkSize.Width, NodeSize.Width) + InterNetworkSpacing;
        double cellHeight = Math.Max(NetworkSize.Height, NodeSize.Height) + NetworkNodeSpacing;

        double startX = 60;
        double startY = 60;

        // Group nodes by network
        var nodesByNetwork = new Dictionary<string, List<NodeDesignModel>>();
        var unassignedNodes = new List<NodeDesignModel>();
        foreach (var node in nodes)
        {
            if (node.Network != null)
            {
                if (!nodesByNetwork.ContainsKey(node.Network))
                    nodesByNetwork[node.Network] = new List<NodeDesignModel>();
                nodesByNetwork[node.Network].Add(node);
            }
            else
            {
                unassignedNodes.Add(node);
            }
        }

        // Calculate grid columns: aim for a roughly square layout
        int totalSlots = networks.Count + nodes.Count;
        int gridCols = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(totalSlots * 1.5)));

        int currentCol = 0;
        int currentRow = 0;

        // Place networks first
        foreach (var net in networks)
        {
            layout.NetworkPositions[net.Name] = new Point(
                startX + currentCol * cellWidth,
                startY + currentRow * cellHeight);

            currentCol++;
            if (currentCol >= gridCols)
            {
                currentCol = 0;
                currentRow++;
            }
        }

        // Place nodes under their network or in remaining grid cells
        foreach (var net in networks)
        {
            if (nodesByNetwork.TryGetValue(net.Name, out var netNodes))
            {
                foreach (var node in netNodes)
                {
                    layout.NodePositions[node.Name] = new Point(
                        startX + currentCol * cellWidth,
                        startY + currentRow * cellHeight);

                    currentCol++;
                    if (currentCol >= gridCols)
                    {
                        currentCol = 0;
                        currentRow++;
                    }
                }
            }
        }

        // Place unassigned nodes
        foreach (var node in unassignedNodes)
        {
            layout.NodePositions[node.Name] = new Point(
                startX + currentCol * cellWidth,
                startY + currentRow * cellHeight);

            currentCol++;
            if (currentCol >= gridCols)
            {
                currentCol = 0;
                currentRow++;
            }
        }

        return layout;
    }

    /// <summary>
    /// Hierarchical layout algorithm.
    /// Level 0: all networks arranged horizontally at the top.
    /// Level 1+: nodes of each network arranged vertically under their network.
    /// Accounts for element sizes in spacing.
    /// </summary>
    public GraphLayoutModel CalculateHierarchicalLayout(NetworkDesignModel model)
    {
        var layout = new GraphLayoutModel();
        var networks = model.Networks.ToList();
        var nodes = model.Nodes.ToList();

        if (networks.Count == 0 && nodes.Count == 0)
            return layout;

        double startX = 60;
        double startY = 60;
        double levelGap = NetworkSize.Height + NodeSize.Height + NetworkNodeSpacing * 3;
        double horizontalSpacing = NetworkSize.Width + InterNetworkSpacing;

        // Group nodes by network
        var nodesByNetwork = new Dictionary<string, List<NodeDesignModel>>();
        var unassignedNodes = new List<NodeDesignModel>();
        foreach (var node in nodes)
        {
            if (node.Network != null)
            {
                if (!nodesByNetwork.ContainsKey(node.Network))
                    nodesByNetwork[node.Network] = new List<NodeDesignModel>();
                nodesByNetwork[node.Network].Add(node);
            }
            else
            {
                unassignedNodes.Add(node);
            }
        }

        // Calculate the maximum number of nodes under any network to determine
        // the vertical space needed per column
        int maxNodesUnderNetwork = nodesByNetwork.Values.Max(nl => nl.Count);
        if (unassignedNodes.Count > maxNodesUnderNetwork)
            maxNodesUnderNetwork = unassignedNodes.Count;

        // Level 0: Place networks horizontally at the top
        double networkY = startY;
        double nodeAreaHeight = Math.Max(1, maxNodesUnderNetwork) * (NodeSize.Height + NetworkNodeSpacing);

        for (int i = 0; i < networks.Count; i++)
        {
            double netX = startX + i * horizontalSpacing;
            layout.NetworkPositions[networks[i].Name] = new Point(netX, networkY);

            // Level 1: Place nodes of this network vertically below
            if (nodesByNetwork.TryGetValue(networks[i].Name, out var netNodes))
            {
                double nodeStartY = networkY + levelGap;
                double columnX = netX;

                // If too many nodes, split into multiple columns
                int nodesPerColumn = Math.Max(3, (int)(400 / (NodeSize.Height + NetworkNodeSpacing)));

                for (int j = 0; j < netNodes.Count; j++)
                {
                    int colIndex = j / nodesPerColumn;
                    int rowIndex = j % nodesPerColumn;
                    double nodeX = columnX + colIndex * (NodeSize.Width + NetworkNodeSpacing);
                    double nodeY = nodeStartY + rowIndex * (NodeSize.Height + NetworkNodeSpacing);

                    layout.NodePositions[netNodes[j].Name] = new Point(nodeX, nodeY);
                }
            }
        }

        // Place unassigned nodes in a separate column on the right
        if (unassignedNodes.Count > 0)
        {
            double unassignedX = startX + networks.Count * horizontalSpacing + InterNetworkSpacing;
            double unassignedY = startY;
            for (int i = 0; i < unassignedNodes.Count; i++)
            {
                layout.NodePositions[unassignedNodes[i].Name] = new Point(
                    unassignedX,
                    unassignedY + i * (NodeSize.Height + NetworkNodeSpacing));
            }
        }

        return layout;
    }

    /// <summary>
    /// Internal helper class for force-directed simulation.
    /// </summary>
    private class ForceElement
    {
        public string Name { get; set; } = "";
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsNetwork { get; set; }
        public string? Network { get; set; }
        public Point Position { get; set; }
    }

    /// <summary>
    /// Simple 2D vector for force calculations.
    /// </summary>
    private readonly struct Vector
    {
        public double X { get; }
        public double Y { get; }

        public Vector(double x, double y)
        {
            X = x;
            Y = y;
        }

        public static Vector operator +(Vector a, Vector b) => new(a.X + b.X, a.Y + b.Y);
        public static Vector operator -(Vector a, Vector b) => new(a.X - b.X, a.Y - b.Y);
        public static Vector operator *(Vector v, double s) => new(v.X * s, v.Y * s);
    }
}