# Withdrawn: laptop results of 21-22 October 2026

These were measured on a laptop where Docker Desktop's VM had 8 GB of memory, shared with
about 30 other containers. During the 100M 4-hop query the whole VM ran out of memory and the
kernel killed NebulaGraph's storage service (`global_oom ... Killed process (nebula-storaged)`).
That was the machine's limit, not NebulaGraph's, so these numbers are not valid and are not
used anywhere. They stay here only so the history is visible. The valid results are in `../`.
