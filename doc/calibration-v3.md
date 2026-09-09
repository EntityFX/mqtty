# Calibration v3

`calibrate --observations <file> --output <new-file> --mqtty-repository <clean-repo>
[--rate-rejection-rate <fraction>]` converts MqttBenchmark observations into a
separate model artifact. Exit 0 means conversion succeeded; exit 1 means invalid
input, dirty repository, or output conflict. No real benchmark is run by this command.

The input must have schemaVersion 3, runCount 288, and exactly the full matrix of
Aedes/Mosquitto/EMQX/ActiveMQ, 16/256 bytes, QoS 0/1/2, and 1/16/64/128 publishers,
with three repeats. MetricStatistics values are consumed through `mean`; their
finite values, physical ranges, count and Student-t CI95 are validated. QoS 0
latency quantiles/statistics must be null. Provenance requires the campaign ID,
configuration/stand hashes, deployment and CPU modes, benchmark commit, and input
hash map. Unknown/missing/duplicate fields, points and incompatible modes fail.

The output has schemaVersion 3, artifactType `broker-calibration`, runCount 288,
profileClientCountMode `publishers`, provenance and points. Each point retains its
broker/messageBytes/qos/publishers/repeats key and contains:

| Field | Meaning |
| --- | --- |
| attemptedRps | Observed mean offered load |
| targetCompletedRps | Observed mean completed publishes per second |
| targetPublishFailureRate | Observed mean fraction of failed attempts |
| rateRejectionRate | Explicit assumed fraction rejected by the model's rate gate |
| capacityRps | attemptedRps × (1 − rateRejectionRate) |
| conditionalPublishFailureRate | (targetPublishFailureRate − rateRejectionRate) / (1 − rateRejectionRate) |
| conditionalDeliveryLossRate | Observed loss among completed publishes, already conditional |
| observedLatencyQuantiles | Original completion latency knots |
| processingLatencyQuantiles | Observed knots minus one RTT for QoS1 or two RTTs for QoS2, each clamped to zero |
| rttBaselineMs | Observed RTT baseline |
| latencyStatus | observed for QoS1/2, notApplicable for QoS0 |

`rateRejectionRate` defaults to zero: capacity is attempted load and all observed
publish failure is assigned to the conditional gate. Observations do not identify
rate rejection separately. A nonzero `--rate-rejection-rate` is an explicit model
assumption applied to every point, not a measured capacity estimate. A point is
rejected if this rate exceeds the target failure; rate 1 is rejected because the
conditional fraction is undefined and admission capacity is zero. The chosen
value is retained per point. Actual rejection depends on tick granularity and
arrival bursts; validate it in the exported RateRejected/Attempted counters before
claiming fidelity. Mean completed/attempted RPS need not reproduce mean failure
across repeats with different attempted rates; both observed targets are retained.

Output provenance retains the complete observation provenance, SHA-256 of the
exact input bytes, the clean MqttY commit and method `conditional-failure-rtt-v1`.
It has no wall-clock timestamp, so the same bytes, commit and assumption reproduce
identical calibration bytes. Runtime validates formulas and RTT subtraction again.
Hash provenance identifies inputs but is not a cryptographic authenticity proof;
the original observations and campaign hash registry remain required evidence.

## Experiment modes

`topologyMode` selects `brokerFidelity` or `mqttRelay` (legacy default). Fidelity
creates N publishers and one subscriber per broker area, without relay applications.
Use one broker for the real-broker comparison. Relay retains existing listeners,
forwarders and receivers. `profileClientCountMode` selects `publishers` (configured
N) or `connectedClients` (all established connections, legacy default). A relay
broker has N + B + 3 connections for B broker areas; fidelity has N + 1. The manifest
records actual connections and the profile lookup count per broker. The runtime
gate and reference metrics use the same selected count. The v3 calibration axis
is publishers; selecting connectedClients against it is an explicit alternative
lookup policy, not a relabelling of the source measurements.

`brokerProfilePath` is read once: the hash and BrokerBenchmarkRepository consume
that same in-memory byte snapshot (decoded with BOM detection). External v3 and v2
profiles are supported. Reference comparisons use v3 observed CompletedRps,
publish-failure rate and completion p99; processing latency is only a model input.
Quantile subtraction is a deterministic approximation assuming a constant RTT,
and simulator ticks/queues still affect measured completion latency. Interpolation
retains observed targets as well as model parameters. Embedded v2 profiles are unchanged.

Three repeats give limited statistical power. Successful conversion and passing
unit tests do not establish real-broker fidelity; run the approved seed matrix and
retain its summaries, counters, manifests and original observation artifacts.
