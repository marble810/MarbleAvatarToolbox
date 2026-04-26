## 1. Session Entry

- [x] 1.1 Add submenu-based editor entry points for starting a VRCFury-free play session and creating a hierarchy copy from a selected avatar
- [x] 1.2 Resolve and validate the selected avatar root before preparing the session

## 2. Clone Session Controller

- [x] 2.1 Implement a controller that creates a temporary avatar clone and records original object state for restoration
- [x] 2.2 Implement VRCFury component detection and stripping on the temporary clone without relying on VRCFury internal APIs
- [x] 2.3 Disable the original avatar, activate the stripped clone, and enter Play Mode through the special session flow

## 3. Cleanup And Validation

- [x] 3.1 Add play mode lifecycle cleanup that removes the clone, restores the original avatar state, and clears session tracking
- [x] 3.2 Add editor feedback for invalid targets, duplicate invocation, and cleanup edge cases
- [ ] 3.3 Validate the workflow against the current project avatar setup and confirm the original scene structure remains unchanged after the session