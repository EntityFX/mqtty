namespace EntityFX.MqttY.Network;

public class TreeBuilder
{
    public class TreeNode
    {
        public int Id { get; set; }
        public List<TreeNode> Children { get; set; } = new List<TreeNode>();
    }


    private int _nextId = 0;

    public TreeNode BuildTree(int branchingFactor, int depth)
    {
        if (branchingFactor < 1 || depth < 1)
            throw new ArgumentException("Параметры должны быть положительными числами");

        return CreateNode(branchingFactor, depth);
    }

    private TreeNode CreateNode(int branchingFactor, int depth)
    {
        var node = new TreeNode { Id = _nextId++ };

        if (depth > 1)
        {
            for (int i = 0; i < branchingFactor; i++)
            {
                var child = CreateNode(branchingFactor, depth - 1);
                node.Children.Add(child);
            }
        }

        return node;
    }

    public void PrintTree(TreeNode root)
    {
        var queue = new Queue<(TreeNode node, int level)>();
        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            var (current, level) = queue.Dequeue();
            Console.WriteLine($"{new string(' ', level * 2)}Узел {current.Id}");

            foreach (var child in current.Children)
                queue.Enqueue((child, level + 1));
        }
    }
}