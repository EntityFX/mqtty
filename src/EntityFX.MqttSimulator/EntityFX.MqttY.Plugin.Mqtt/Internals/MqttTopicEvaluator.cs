using System.Collections.Concurrent;
using EntityFX.MqttY.Contracts.Mqtt;

namespace EntityFX.MqttY.Plugin.Mqtt.Internals
{
    /// <summary>
    /// Represents an evaluator for MQTT topics
    /// according to the rules defined in the protocol specification
    /// </summary>
    /// See <a href="http://docs.oasis-open.org/mqtt/mqtt/v3.1.1/mqtt-v3.1.1.html#_Toc442180919">Topic Names and Topic Filters</a>
    /// for more details on the topics specification
    internal class MqttTopicEvaluator : IMqttTopicEvaluator
    {
        /// <summary>
        /// Character that defines the single level topic wildcard, which is '+'
        /// </summary>
        public const string SingleLevelTopicWildcard = "+";

        /// <summary>
        /// Character that defines the multi level topic wildcard, which is '#'
        /// </summary>
        public const string MultiLevelTopicWildcard = "#";

        // A publish is matched against the same small set of filters every tick;
        // splitting topic/filter strings on every match dominated fanout CPU.
        private const int MaxCachedParts = 8192;
        private readonly ConcurrentDictionary<string, TopicParts> _parts = new(StringComparer.Ordinal);
        private int _cachedParts;

        public bool AllowWildcardsInTopicFilters { get;  }

        /// <summary>
        /// Initializes a new instance of the <see cref="MqttTopicEvaluator" /> class,
        /// specifying the configuration to use
        /// </summary>
        /// <param name="configuration">
        /// Configuration to use by the evaluator.
        /// See <see cref="MqttConfiguration" /> for more details about the configuration options
        /// </param>
        public MqttTopicEvaluator(bool allowWildcardsInTopicFilters)
        {
            AllowWildcardsInTopicFilters = allowWildcardsInTopicFilters;
        }

        /// <summary>
        /// Determines if a topic filter is valid according to the protocol specification
        /// </summary>
        /// <param name="topicFilter">Topic filter to evaluate</param>
        /// <returns>A boolean value that indicates if the topic filter is valid or not</returns>
        public bool IsValidTopicFilter(string topicFilter)
        {
            if (string.IsNullOrEmpty(topicFilter))
                return false;

            return GetParts(topicFilter).ValidFilter;
        }

        /// <summary>
        /// Determines if a topic name is valid according to the protocol specification
        /// </summary>
        /// <param name="topicName">Topic name to evaluate</param>
        /// <returns>A boolean value that indicates if the topic name is valid or not</returns>
        public bool IsValidTopicName(string topicName) =>
            !string.IsNullOrEmpty(topicName) && GetParts(topicName).ValidName;

        /// <summary>
        /// Evaluates if a topic name applies to a specific topic filter
        /// If a topic name matches a filter, it means that the Server will
        /// successfully dispatch incoming messages of that topic name
        /// to the subscribers of the topic filter
        /// </summary>
        /// <param name="topicName">Topic name to evaluate</param>
        /// <param name="topicFilter">Topic filter to evaluate</param>
        /// <returns>A boolean value that indicates if the topic name matches with the topic filter</returns>
        /// <exception cref="MqttException">MqttException</exception>
        public bool Matches(string topicName, string topicFilter)
        {
            if (string.IsNullOrEmpty(topicName))
            {
                throw new MqttException($"Invalid Topic Name {topicName}");
            }

            if (string.IsNullOrEmpty(topicFilter))
            {
                throw new MqttException($"Invalid Topic Filter {topicFilter}");
            }

            var topic = GetParts(topicName);

            if (!topic.ValidName)
            {
                throw new MqttException($"Invalid Topic Name {topicName}");
            }

            var filter = GetParts(topicFilter);

            if (!filter.ValidFilter)
            {
                throw new MqttException($"Invalid Topic Filter {topicFilter}");
            }

            var topicFilterParts = filter.Segments;
            var topicNameParts = topic.Segments;

            if (topicNameParts.Length > topicFilterParts.Length && topicFilterParts[topicFilterParts.Length - 1] != "#")
                return false;

            if (topicFilterParts.Length - topicNameParts.Length > 1)
                return false;

            if (topicFilterParts.Length - topicNameParts.Length == 1 && topicFilterParts[topicFilterParts.Length - 1] != "#")
                return false;

            if ((topicFilterParts[0] == "#" || topicFilterParts[0] == "+") && topicNameParts[0].StartsWith("$"))
                return false;

            var matches = true;

            for (var i = 0; i < topicFilterParts.Length; i++)
            {
                var topicFilterPart = topicFilterParts[i];

                if (topicFilterPart == "#")
                {
                    matches = true;
                    break;
                }

                if (topicFilterPart == "+")
                {
                    if (i == topicFilterParts.Length - 1 && topicNameParts.Length > topicFilterParts.Length)
                    {
                        matches = false;
                        break;
                    }

                    continue;
                }

                if (topicFilterPart != topicNameParts[i])
                {
                    matches = false;
                    break;
                }
            }

            return matches;
        }

        private TopicParts GetParts(string text)
        {
            if (_parts.TryGetValue(text, out var parts))
            {
                return parts;
            }

            parts = BuildParts(text);
            if (Volatile.Read(ref _cachedParts) < MaxCachedParts && _parts.TryAdd(text, parts))
            {
                Interlocked.Increment(ref _cachedParts);
            }

            return parts;
        }

        private TopicParts BuildParts(string text)
        {
            if (text.Length == 0)
            {
                return new TopicParts(new[] { string.Empty }, ValidName: false, ValidFilter: false);
            }

            var segments = text.Split('/');
            var validName = text.Length <= ushort.MaxValue &&
                !text.Contains(MultiLevelTopicWildcard) &&
                !text.Contains(SingleLevelTopicWildcard);

            if (!AllowWildcardsInTopicFilters)
            {
                return new TopicParts(segments, validName, validName);
            }

            return new TopicParts(segments, validName, IsValidFilter(text, segments));
        }

        private static bool IsValidFilter(string text, string[] segments)
        {
            if (text.Length > ushort.MaxValue)
                return false;

            var multiLevelCount = 0;

            foreach (var segment in segments)
            {
                if (segment == MultiLevelTopicWildcard)
                {
                    multiLevelCount++;
                }
                else if ((segment.Length > 1 && segment.Contains(MultiLevelTopicWildcard)) ||
                         (segment.Length > 1 && segment.Contains(SingleLevelTopicWildcard)))
                {
                    return false;
                }
            }

            if (multiLevelCount > 1)
                return false;

            if (multiLevelCount == 1 && text.IndexOf(MultiLevelTopicWildcard, StringComparison.Ordinal) < text.Length - 1)
                return false;

            return true;
        }

        private readonly record struct TopicParts(string[] Segments, bool ValidName, bool ValidFilter);
    }
}
