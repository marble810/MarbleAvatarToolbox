## ADDED Requirements

### Requirement: Mirror Keyframes SHALL use reference-space mirror planes
The system SHALL interpret the mirror axis selected in KeyframeMirror as a mirror plane in the local space of the Animation Window active root object. Axis X MUST mean reflection across the active root's YZ plane, axis Y MUST mean reflection across the active root's XZ plane, and axis Z MUST mean reflection across the active root's XY plane.

#### Scenario: X axis maps to YZ plane
- **WHEN** the user runs Mirror Keyframes with mirror axis set to X
- **THEN** the system uses the active root object's local YZ plane as the mirror plane for the operation

#### Scenario: Mirror operation requires an active root
- **WHEN** the user runs Mirror Keyframes and the Animation Window cannot resolve an active root object
- **THEN** the system rejects the operation and reports that the mirror reference root is unavailable

### Requirement: Mirror Keyframes SHALL operate on complete transform samples
The system SHALL aggregate selected keyframes by Transform and time. If any position or rotation-related channel of a Transform is selected at a given time, the system MUST reconstruct the complete transform sample for that Transform at that time before performing the mirror operation.

#### Scenario: Partial channel selection expands to full transform sample
- **WHEN** the user selects only one position or rotation channel for a Transform at time t and runs Mirror Keyframes
- **THEN** the system mirrors the complete transform sample for that Transform at time t instead of modifying only the selected scalar channel

#### Scenario: Multiple selected channels at the same time are processed once
- **WHEN** the user selects multiple channels for the same Transform at the same time
- **THEN** the system treats them as a single mirrored transform sample and writes back one consistent mirrored result

### Requirement: Mirror Keyframes SHALL perform spatial reflection instead of scalar negation
The system SHALL compute mirrored transform results through spatial reflection in the reference space. It MUST NOT define Mirror Keyframes as direct sign inversion of individual curve values.

#### Scenario: Rotation mirroring is not derived from string-based scalar rules
- **WHEN** the selected transform sample includes rotation data
- **THEN** the system computes the mirrored rotation from the transform sample in space instead of applying per-axis string matching and scalar negation rules

#### Scenario: Non-transform curves remain outside mirror semantics
- **WHEN** a selected keyframe does not belong to a supported Transform position or rotation channel
- **THEN** the system leaves that curve outside the Mirror Keyframes operation

### Requirement: Mirror Keyframes SHALL preserve the clip's transform curve representation
The system SHALL write mirrored data back using the same transform curve representation already used by the clip for the affected channels. If the clip uses Euler rotation curves, the mirrored result MUST be written back as Euler curves. If the clip uses quaternion rotation curves, the mirrored result MUST be written back as quaternion curves.

#### Scenario: Euler clip stays Euler
- **WHEN** the affected transform sample is stored with Euler rotation curves
- **THEN** the mirrored result is written back through the corresponding Euler rotation curve group

#### Scenario: Quaternion clip stays quaternion
- **WHEN** the affected transform sample is stored with quaternion rotation curves
- **THEN** the mirrored result is written back through the corresponding quaternion rotation curve group

### Requirement: Key Symmetry SHALL remain distinct from Mirror Keyframes
The system SHALL treat Key Symmetry as a source-to-target symmetry operation based on mapped left/right transforms, while Mirror Keyframes SHALL remain a self-mirroring operation on the selected transform samples.

#### Scenario: Mirror Keyframes writes back to the same transform
- **WHEN** the user runs Mirror Keyframes
- **THEN** the mirrored result is written back to the same selected Transform sample set

#### Scenario: Key Symmetry writes to paired target transforms
- **WHEN** the user runs Key Symmetry with mapped source and target transforms
- **THEN** the system mirrors the source transform samples and writes the result to the mapped target transforms instead of overwriting the source set