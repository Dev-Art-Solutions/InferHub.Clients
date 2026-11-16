# python client 1.1.1 — the loaded fraction of a group that is already running

Found by installing 1.1.0 from the registry and running it against a real split model: the loaded fraction of a job's group read 16 % for a group that was running. A member that kept its layers from an earlier load is sent only a part of its share, so "sent of expected" says nothing once the group runs. It is now absent for a running group, and documented as a figure for a group that is coming up — one that can stop short of 1 for the same reason.

Nothing else changed. The same fix is in all four clients, with a test in each.
