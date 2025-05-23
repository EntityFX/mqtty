﻿using EntityFX.MqttY.Plugin.Mqtt.Contracts;

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
            if (!AllowWildcardsInTopicFilters)
            {
                if (topicFilter.Contains(SingleLevelTopicWildcard) ||
                    topicFilter.Contains(MultiLevelTopicWildcard))
                    return false;

            }

            if (string.IsNullOrEmpty(topicFilter))
                return false;

            if (topicFilter.Length > 65536)
                return false;

            var topicFilterParts = topicFilter.Split('/');

            if (topicFilterParts.Count(s => s == "#") > 1)
                return false;

            if (topicFilterParts.Any(s => s.Length > 1 && s.Contains("#")))
                return false;

            if (topicFilterParts.Any(s => s.Length > 1 && s.Contains("+")))
                return false;

            if (topicFilterParts.Any(s => s == "#") && topicFilter.IndexOf("#") < topicFilter.Length - 1)
                return false;

            return true;
        }

        /// <summary>
        /// Determines if a topic name is valid according to the protocol specification
        /// </summary>
        /// <param name="topicName">Topic name to evaluate</param>
        /// <returns>A boolean value that indicates if the topic name is valid or not</returns>
        public bool IsValidTopicName(string topicName)
        {
            return !string.IsNullOrEmpty(topicName) &&
                topicName.Length <= 65536 &&
                !topicName.Contains("#") &&
                !topicName.Contains("+");
        }

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
            if (!IsValidTopicName(topicName))
            {
                throw new MqttException($"Invalid Topic Name {topicName}");
            }

            if (!IsValidTopicFilter(topicFilter))
            {
                throw new MqttException($"Invalid Topic Filter {topicFilter}");
            }

            var topicFilterParts = topicFilter.Split('/');
            var topicNameParts = topicName.Split('/');

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
    }

}
