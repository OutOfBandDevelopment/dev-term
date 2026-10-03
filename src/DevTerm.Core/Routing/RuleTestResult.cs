namespace DevTerm.Core.Routing;

/// <summary>What a rule would do with a sample: the topic and the payload (device to broker) or the text sent to the device (broker to device).</summary>
public sealed record RuleTestResult(string Topic, string Text);
