#!/bin/bash
# Auto-deploy: kill game, build, deploy, launch
PID_FILE="D:/dev/mod-dev/godot/Godot_v4.5.1-stable_mono_win64/mods/PredictEverything/logs/pid.txt"
LOCAL_DLL="D:/project/game/StS2/SlayTheSpire2/mods/PredictEverything/predict_everything.dll"
GODOT_DLL="D:/dev/mod-dev/godot/Godot_v4.5.1-stable_mono_win64/mods/PredictEverything/predict_everything.dll"
GODOT_EXE="D:/dev/mod-dev/godot/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64.exe"
PROJECT_DIR="D:/project/game/StS2/SlayTheSpire2"

# 1. Kill running game
if [ -f "$PID_FILE" ]; then
    PID=$(cat "$PID_FILE")
    if kill -0 "$PID" 2>/dev/null; then
        echo "Killing game (PID $PID)..."
        powershell -Command "Stop-Process -Id $PID -Force" 2>/dev/null
        sleep 1
        echo "Killed."
    fi
fi

# 2. Build
cd "$(dirname "$0")"
echo "Building..."
dotnet build PredictEverything.csproj --nologo -v q
if [ $? -ne 0 ]; then
    echo "BUILD FAILED"
    exit 1
fi

# 3. Deploy
cp bin/Debug/net9.0/predict_everything.dll "$LOCAL_DLL"
cp "$LOCAL_DLL" "$GODOT_DLL"
echo "Deployed ($(ls -lh "$GODOT_DLL" | awk '{print $5}'))"

# 4. Launch game
echo "Launching..."
"$GODOT_EXE" --path "$PROJECT_DIR" &
echo "Done."
