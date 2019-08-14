#!/bin/bash

# Add local user
# Either use the LOCAL_USER_ID if passed in at runtime or fallback

# 999 is the uid of the buildkite-agent, which we replicate here to avoid file permission issues for files written
# to shared volumes.
USER_ID=${LOCAL_USER_ID:-999}

useradd --shell /bin/bash -u "${USER_ID}" -o -c "" -m user
export HOME=/home/user
chown -R user:user "${HOME}"
chown -R user:user "./"
chown -R user:user "/var/spatial_oauth"

tail -f /dev/null
