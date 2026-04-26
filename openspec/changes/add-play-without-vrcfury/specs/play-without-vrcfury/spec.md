## ADDED Requirements

### Requirement: User can start a VRCFury-free play session for an avatar
The editor tooling SHALL provide a dedicated action that starts Play for a chosen avatar through a special session that excludes VRCFury components from the play target.

#### Scenario: Start special play session from a valid avatar
- **WHEN** the user invokes the dedicated action while a valid avatar root is selected
- **THEN** the tool SHALL prepare a temporary play target for that avatar and enter Play Mode through that special session

#### Scenario: Reject invalid play target
- **WHEN** the user invokes the dedicated action without a resolvable avatar root
- **THEN** the tool SHALL refuse to start the special play session and SHALL show an editor-visible error message

### Requirement: User can create a VRCFury-free hierarchy copy without entering Play
The editor tooling SHALL provide a dedicated action that creates a stripped avatar copy in the current scene hierarchy without entering Play Mode.

#### Scenario: Create hierarchy copy from a valid avatar
- **WHEN** the user invokes the hierarchy-copy action for a resolvable avatar root
- **THEN** the tool SHALL create a new avatar copy in the hierarchy with VRCFury components removed and SHALL disable the original avatar

#### Scenario: Copy action shares the same avatar resolution rules
- **WHEN** the user invokes the hierarchy-copy action without a direct valid selection
- **THEN** the tool SHALL use the same fallback avatar resolution behavior as the play action or show an editor-visible error message if the target is still ambiguous

### Requirement: Related VRCFury-free actions are grouped under a shared submenu
The editor tooling SHALL expose the play action and the hierarchy-copy action under the same submenu so the workflow is discoverable from one location.

#### Scenario: Shared submenu contains both actions
- **WHEN** the user opens the VRCFury-free tools submenu
- **THEN** the editor SHALL show both the play action and the hierarchy-copy action

### Requirement: The special play session must not mutate the original avatar structure
The tool SHALL preserve the original avatar hierarchy, components, and serialized scene data by performing VRCFury exclusion only on a temporary clone.

#### Scenario: Original avatar remains unchanged before Play
- **WHEN** the special play session is prepared
- **THEN** the original avatar SHALL remain in the scene without permanent component removal or asset modification

#### Scenario: Original avatar is restored after Play
- **WHEN** the editor returns to EditMode after a special play session
- **THEN** the tool SHALL remove the temporary clone and SHALL restore the original avatar active state

### Requirement: The temporary play target excludes VRCFury components
Before entering Play, the tool SHALL remove VRCFury-related components from the temporary play target so that VRCFury component-driven preprocessing does not run on that target.

#### Scenario: Strip VRCFury components from clone
- **WHEN** the temporary play target is created
- **THEN** the tool SHALL remove components identified as belonging to VRCFury from the clone before Play starts

#### Scenario: Keep non-VRCFury components intact on clone
- **WHEN** VRCFury components are stripped from the temporary play target
- **THEN** the tool SHALL preserve non-VRCFury components needed for avatar testing, including standard VRChat and optimization components

### Requirement: The tool must manage a single explicit special-play session lifecycle
The tool SHALL track whether a special play session is active and SHALL handle cleanup, duplicate invocation, and missing-object cases safely.

#### Scenario: Prevent duplicate special sessions
- **WHEN** the user invokes the dedicated action while a prior special play session is already prepared or running
- **THEN** the tool SHALL reject or reuse the request in a deterministic way and SHALL not create uncontrolled duplicate clones

#### Scenario: Cleanup tolerates missing objects
- **WHEN** the editor returns to EditMode and either the original avatar or the temporary clone has already been deleted
- **THEN** the tool SHALL finish cleanup without throwing uncaught exceptions and SHALL reset its internal session state