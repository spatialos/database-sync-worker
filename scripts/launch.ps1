$ErrorActionPreference = "Stop"

cd "$PSScriptRoot/../"

& spatial alpha local launch --main_config=./config/spatialos.json --launch_config=config/deployment.json
return $?
