#!/bin/bash
# Auto-deploy: kill game, build, deploy DLL + locales, launch
GODOT_EXE="D:/dev/mod-dev/godot/Godot_v4.5.1-stable_mono_win64/Godot_v4.5.1-stable_mono_win64.exe"
PROJECT_DIR="D:/project/game/StS2/SlayTheSpire2-v0.107.1"
MOD_DIR="D:/project/game/StS2/all-mods/PredictEverything"
RUNTIME_DIR="D:/dev/mod-dev/godot/Godot_v4.5.1-stable_mono_win64/mods/PredictEverything"
# 1. Kill ALL Godot processes and wait for DLL to unlock
echo "Killing Godot..."
powershell -Command "Get-Process -Name 'Godot*' -ErrorAction SilentlyContinue | Stop-Process -Force" 2>/dev/null
for i in 1 2 3 4 5; do
    if [ -f "$RUNTIME_DIR/predict_everything.dll" ]; then
        if cp "$RUNTIME_DIR/predict_everything.dll" "$RUNTIME_DIR/predict_everything.dll.test" 2>/dev/null; then
            rm -f "$RUNTIME_DIR/predict_everything.dll.test" 2>/dev/null
            break
        fi
    fi
    echo "  Waiting ($i)..."
    sleep 1
done

# 2. Build
cd "$MOD_DIR"
echo "Building..."
dotnet build PredictEverything.csproj --nologo -v q
if [ $? -ne 0 ]; then
    echo "BUILD FAILED"
    exit 1
fi

# 3. Deploy DLL + locales to dev runtime
cp bin/Debug/net9.0/predict_everything.dll "$MOD_DIR/predict_everything.dll"
cp "$MOD_DIR/predict_everything.dll" "$RUNTIME_DIR/predict_everything.dll"
cp "$MOD_DIR/locale/zh.json" "$RUNTIME_DIR/locale/zh.json"
cp "$MOD_DIR/locale/en.json" "$RUNTIME_DIR/locale/en.json"
echo "Deployed dev ($(ls -lh "$RUNTIME_DIR/predict_everything.dll" | awk '{print $5}'))"

# 4. Launch
echo "Launching..."
"$GODOT_EXE" --path "$PROJECT_DIR" &
echo "Done."
