#!/usr/bin/env bash
# FidelSec Forensic Imager — Linux build & run script
#
# Usage:
#   sudo ./disk-imager.sh            # Debug build + run
#   sudo ./disk-imager.sh --release  # Release build + run
#   sudo ./disk-imager.sh --build-only
#   sudo ./disk-imager.sh --test     # Run unit tests
# ============================================================
 
set -euo pipefail
 
# ── Colours ────────────────────────────────────────────────
RED='\033[0;31m'; YELLOW='\033[1;33m'; GREEN='\033[0;32m'
CYAN='\033[0;36m'; BOLD='\033[1m'; DIM='\033[2m'; NC='\033[0m'
 
# ── Root check ─────────────────────────────────────────────
if [[ $EUID -ne 0 ]]; then
  echo -e "${RED}✗ This script must be run as root (sudo).${NC}" >&2
  exit 1
fi
 
# ── Dependency check ───────────────────────────────────────
for cmd in lsblk dd; do
  command -v "$cmd" &>/dev/null || { echo -e "${RED}✗ Required command '$cmd' not found.${NC}"; exit 1; }
done
 
# ── Header ─────────────────────────────────────────────────
clear
echo -e "${BOLD}${CYAN}"
cat << 'EOF'
  ██████╗ ██╗███████╗██╗  ██╗    ██╗███╗   ███╗ █████╗  ██████╗ ███████╗██████╗
  ██╔══██╗██║██╔════╝██║ ██╔╝    ██║████╗ ████║██╔══██╗██╔════╝ ██╔════╝██╔══██╗
  ██║  ██║██║███████╗█████╔╝     ██║██╔████╔██║███████║██║  ███╗█████╗  ██████╔╝
  ██║  ██║██║╚════██║██╔═██╗     ██║██║╚██╔╝██║██╔══██║██║   ██║██╔══╝  ██╔══██╗
  ██████╔╝██║███████║██║  ██╗    ██║██║ ╚═╝ ██║██║  ██║╚██████╔╝███████╗██║  ██║
  ╚═════╝ ╚═╝╚══════╝╚═╝  ╚═╝   ╚═╝╚═╝     ╚═╝╚═╝  ╚═╝ ╚═════╝ ╚══════╝╚═╝  ╚═╝
EOF
echo -e "${NC}${DIM}  Raw disk imaging tool — use with caution${NC}"
echo ""
 
# ── Discover drives ────────────────────────────────────────
echo -e "${BOLD}Scanning for block devices...${NC}"
echo ""
 
# Build arrays of drives (exclude loop, ram, zram, sr devices)
mapfile -t DRIVES < <(lsblk -d -o NAME,SIZE,MODEL,TYPE --noheadings \
  | awk '$4 == "disk" {print $1}' \
  | grep -v '^loop\|^ram\|^zram')
 
if [[ ${#DRIVES[@]} -eq 0 ]]; then
  echo -e "${RED}✗ No block devices found.${NC}"
  exit 1
fi
 
# ── Display drive table ────────────────────────────────────
printf "  ${BOLD}%-4s  %-10s  %-10s  %-30s  %s${NC}\n" "#" "DEVICE" "SIZE" "MODEL" "PARTITIONS"
echo -e "  ${DIM}$(printf '%.0s─' {1..75})${NC}"
 
declare -A DRIVE_MAP
idx=1
for dev in "${DRIVES[@]}"; do
  SIZE=$(lsblk -d -o SIZE --noheadings "/dev/$dev" 2>/dev/null | tr -d ' ')
  MODEL=$(lsblk -d -o MODEL --noheadings "/dev/$dev" 2>/dev/null | xargs)
  PARTS=$(lsblk -o NAME --noheadings "/dev/$dev" 2>/dev/null | grep -v "^$dev$" | xargs | tr ' ' ',' || true)
  [[ -z "$MODEL" ]] && MODEL="${DIM}(no model)${NC}"
  [[ -z "$PARTS" ]] && PARTS="${DIM}none${NC}"
  printf "  ${CYAN}%-4s${NC}  ${BOLD}/dev/%-6s${NC}  %-10s  %-30s  %b\n" \
    "[$idx]" "$dev" "$SIZE" "$MODEL" "$PARTS"
  DRIVE_MAP[$idx]=$dev
  (( idx++ ))
done
 
echo ""
 
# ── Select source drive ────────────────────────────────────
while true; do
  read -rp "$(echo -e "  ${BOLD}Select source drive to image [1-$((idx-1))]: ${NC}")" SEL
  if [[ "$SEL" =~ ^[0-9]+$ ]] && [[ -n "${DRIVE_MAP[$SEL]+_}" ]]; then
    SRC_DEV="/dev/${DRIVE_MAP[$SEL]}"
    break
  fi
  echo -e "  ${RED}Invalid selection. Try again.${NC}"
done
 
echo ""
echo -e "  ${GREEN}✔ Selected:${NC} ${BOLD}$SRC_DEV${NC}"
echo ""
 
# ── Output file ────────────────────────────────────────────
DEFAULT_OUT="$(pwd)/disk_$(basename "$SRC_DEV")_$(date +%Y%m%d_%H%M%S).img"
read -rp "$(echo -e "  ${BOLD}Output image path [${DIM}$DEFAULT_OUT${NC}${BOLD}]: ${NC}")" OUT_FILE
[[ -z "$OUT_FILE" ]] && OUT_FILE="$DEFAULT_OUT"
 
# Check destination directory exists
OUT_DIR=$(dirname "$OUT_FILE")
if [[ ! -d "$OUT_DIR" ]]; then
  echo -e "  ${RED}✗ Directory '$OUT_DIR' does not exist.${NC}"
  exit 1
fi
 
# ── Block size ─────────────────────────────────────────────
echo ""
echo -e "  ${BOLD}Block size options:${NC}"
echo -e "  ${CYAN}[1]${NC} 4M   — fast, good for most drives"
echo -e "  ${CYAN}[2]${NC} 1M   — safer for older/smaller drives"
echo -e "  ${CYAN}[3]${NC} 512  — forensic-safe, slowest"
echo -e "  ${CYAN}[4]${NC} Custom"
echo ""
read -rp "$(echo -e "  ${BOLD}Block size [1]: ${NC}")" BS_SEL
case "${BS_SEL:-1}" in
  1) BS="4M" ;;
  2) BS="1M" ;;
  3) BS="512" ;;
  4) read -rp "  Enter block size (e.g. 2M, 64K, 4096): " BS ;;
  *) BS="4M" ;;
esac
 
# ── Compression option ─────────────────────────────────────
echo ""
echo -e "  ${BOLD}Compression:${NC}"
echo -e "  ${CYAN}[1]${NC} None  — raw .img (fastest, largest)"
if command -v gzip &>/dev/null; then
  echo -e "  ${CYAN}[2]${NC} gzip  — .img.gz"
fi
if command -v pigz &>/dev/null; then
  echo -e "  ${CYAN}[3]${NC} pigz  — .img.gz (parallel gzip, faster)"
fi
if command -v xz &>/dev/null; then
  echo -e "  ${CYAN}[4]${NC} xz    — .img.xz (smallest, slowest)"
fi
echo ""
read -rp "$(echo -e "  ${BOLD}Compression [1]: ${NC}")" COMP_SEL
 
case "${COMP_SEL:-1}" in
  2) COMPRESS_CMD="gzip -c"; OUT_FILE="${OUT_FILE%.img}.img.gz" ;;
  3) COMPRESS_CMD="pigz -c"; OUT_FILE="${OUT_FILE%.img}.img.gz" ;;
  4) COMPRESS_CMD="xz -c";   OUT_FILE="${OUT_FILE%.img}.img.xz" ;;
  *) COMPRESS_CMD="" ;;
esac
 
# ── Summary & confirm ──────────────────────────────────────
SRC_SIZE=$(lsblk -d -o SIZE --noheadings "$SRC_DEV" | tr -d ' ')
echo ""
echo -e "  ${DIM}$(printf '%.0s─' {1..60})${NC}"
echo -e "  ${BOLD}Summary${NC}"
echo -e "  ${DIM}$(printf '%.0s─' {1..60})${NC}"
echo -e "  Source   : ${BOLD}${CYAN}$SRC_DEV${NC}  (${SRC_SIZE})"
echo -e "  Output   : ${BOLD}$OUT_FILE${NC}"
echo -e "  Block sz : $BS"
echo -e "  Compress : ${COMPRESS_CMD:-none}"
echo -e "  ${DIM}$(printf '%.0s─' {1..60})${NC}"
echo ""
echo -e "  ${YELLOW}⚠  WARNING: Do NOT select your system / boot drive unless you know${NC}"
echo -e "  ${YELLOW}   what you're doing. This is a READ-ONLY operation, but proceed${NC}"
echo -e "  ${YELLOW}   carefully. Ensure you have enough free disk space.${NC}"
echo ""
read -rp "$(echo -e "  ${BOLD}Type 'yes' to start imaging: ${NC}")" CONFIRM
if [[ "$CONFIRM" != "yes" ]]; then
  echo -e "\n  ${RED}Aborted.${NC}"
  exit 0
fi
 
# ── Imaging ────────────────────────────────────────────────
echo ""
echo -e "  ${GREEN}▶ Starting image...${NC}  ${DIM}(Ctrl+C to abort)${NC}"
echo ""
 
START_TIME=$(date +%s)
 
# Check if pv is available for a progress bar
if command -v pv &>/dev/null; then
  SRC_BYTES=$(blockdev --getsize64 "$SRC_DEV" 2>/dev/null || echo 0)
  if [[ -n "$COMPRESS_CMD" ]]; then
    dd if="$SRC_DEV" bs="$BS" status=none | pv -s "$SRC_BYTES" | $COMPRESS_CMD > "$OUT_FILE"
  else
    dd if="$SRC_DEV" bs="$BS" status=none | pv -s "$SRC_BYTES" > "$OUT_FILE"
  fi
else
  # Fall back to dd's built-in progress
  if [[ -n "$COMPRESS_CMD" ]]; then
    dd if="$SRC_DEV" bs="$BS" status=progress | $COMPRESS_CMD > "$OUT_FILE"
  else
    dd if="$SRC_DEV" bs="$BS" status=progress of="$OUT_FILE"
  fi
fi
 
END_TIME=$(date +%s)
ELAPSED=$(( END_TIME - START_TIME ))
ELAPSED_FMT=$(printf '%02d:%02d:%02d' $((ELAPSED/3600)) $((ELAPSED%3600/60)) $((ELAPSED%60)))
 
# ── Done ───────────────────────────────────────────────────
echo ""
echo -e "  ${GREEN}${BOLD}✔ Image created successfully!${NC}"
echo ""
 
IMG_SIZE=$(du -sh "$OUT_FILE" 2>/dev/null | cut -f1)
echo -e "  File   : ${BOLD}$OUT_FILE${NC}"
echo -e "  Size   : $IMG_SIZE"
echo -e "  Time   : $ELAPSED_FMT"
 
# Generate SHA256 checksum
echo ""
read -rp "$(echo -e "  ${BOLD}Generate SHA256 checksum? [Y/n]: ${NC}")" DO_HASH
if [[ "${DO_HASH,,}" != "n" ]]; then
  echo -e "  ${DIM}Computing SHA256...${NC}"
  sha256sum "$OUT_FILE" | tee "${OUT_FILE}.sha256"
  echo -e "  ${GREEN}✔ Checksum saved to ${OUT_FILE}.sha256${NC}"
fi
 
echo ""
echo -e "  ${DIM}Tip: To restore this image later, run:${NC}"
echo -e "  ${CYAN}  sudo dd if=\"$OUT_FILE\" of=/dev/<target> bs=$BS status=progress${NC}"
echo ""
 