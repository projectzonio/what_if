#!/usr/bin/env python
"""RG-107 DEM reader: Copernicus GLO-30 COG tiles (stdlib only) -> bilinear height sampling.

The GLO-30 tiles on the AWS Open Data registry are Cloud Optimized GeoTIFFs: float32, DEFLATE, floating-point predictor (TIFF predictor 3), tiled.
This module reads exactly what is needed from the first IFD (classic TIFF and BigTIFF), decodes tiles lazily and samples heights.
Anything else (other compression or predictor) raises DemError, it is never guessed.

Source : https://copernicus-dem-30m.s3.amazonaws.com/ (public bucket, no registration; registry https://registry.opendata.aws/copernicus-dem/)
Licence: free for the general public under the Copernicus DEM licence (ESA); credit text in DEM_CREDIT below.
"""
import hashlib
import json
import math
import os
import struct
import time
import urllib.request
import zlib
from array import array
from itertools import accumulate

DEM_URL_PATTERN = ("https://copernicus-dem-30m.s3.amazonaws.com/Copernicus_DSM_COG_10_{ns}{lat:02d}_00_{ew}{lon:03d}_00_DEM/"
                   "Copernicus_DSM_COG_10_{ns}{lat:02d}_00_{ew}{lon:03d}_00_DEM.tif")
DEM_LICENCE = ("Copernicus DEM GLO-30: free of charge for the general public under the Copernicus DEM licence "
               "(https://dataspace.copernicus.eu/explore-data/data-collections/copernicus-contributing-missions/collections-description/COP-DEM); "
               "AWS registry https://registry.opendata.aws/copernicus-dem/ , no registration for the public bucket")
DEM_CREDIT = ("© DLR e.V. 2010-2014 and © Airbus Defence and Space GmbH 2014-2018 provided under COPERNICUS by the European Union and ESA; all rights reserved. "
              "Copernicus Digital Elevation Model (DEM) was accessed on {date} from https://registry.opendata.aws/copernicus-dem.")
NODATA_BELOW = -1000.0  # GLO-30 void/nodata markers are far below any real surface height

_TYPE_SIZE = {1: 1, 2: 1, 3: 2, 4: 4, 5: 8, 6: 1, 7: 1, 8: 2, 9: 4, 10: 8, 11: 4, 12: 8, 16: 8, 17: 8, 18: 8}
_TYPE_FMT = {1: "B", 2: "c", 3: "H", 4: "I", 6: "b", 7: "B", 8: "h", 9: "i", 11: "f", 12: "d", 16: "Q", 17: "q"}


class DemError(Exception):
    pass


def tile_names(bbox):
    """Integer (lat, lon) south-west corners of the 1-degree tiles that cover bbox = (south, west, north, east)."""
    s, w, n, e = bbox
    out = []
    for lat in range(int(math.floor(s)), int(math.floor(n)) + 1):
        for lon in range(int(math.floor(w)), int(math.floor(e)) + 1):
            out.append((lat, lon))
    return out


def tile_url(lat, lon):
    return DEM_URL_PATTERN.format(ns="N" if lat >= 0 else "S", lat=abs(lat), ew="E" if lon >= 0 else "W", lon=abs(lon))


def tile_file(lat, lon):
    return "dem_%s%02d_%s%03d.tif" % ("N" if lat >= 0 else "S", abs(lat), "E" if lon >= 0 else "W", abs(lon))


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def fetch_tiles(bbox, dem_dir, log=print):
    """Download the tiles that cover bbox into dem_dir (cache: an existing file with a sidecar is not fetched again).
    Returns the list of provenance dicts (url, date, bytes, sha256, licence, credit, path)."""
    os.makedirs(dem_dir, exist_ok=True)
    metas = []
    for lat, lon in tile_names(bbox):
        path = os.path.join(dem_dir, tile_file(lat, lon))
        side = path + ".meta.json"
        if not (os.path.exists(path) and os.path.exists(side)):
            url = tile_url(lat, lon)
            log("dem: GET %s" % url)
            req = urllib.request.Request(url, headers={"User-Agent": "NightRacer-worldgen/0.1 (personal noncommercial research)"})
            tmp = path + ".part"
            with urllib.request.urlopen(req, timeout=120) as r, open(tmp, "wb") as f:
                while True:
                    chunk = r.read(1 << 20)
                    if not chunk:
                        break
                    f.write(chunk)
            os.replace(tmp, path)
            date = time.strftime("%Y-%m-%d", time.gmtime())
            meta = {"kind": "dem-tile", "url": url, "date": date, "bytes": os.path.getsize(path), "sha256": sha256_file(path),
                    "licence": DEM_LICENCE, "credit": DEM_CREDIT.format(date=date), "file": os.path.basename(path)}
            with open(side, "w", encoding="utf-8") as f:
                json.dump(meta, f, indent=1)
        with open(side, encoding="utf-8") as f:
            metas.append(json.load(f))
    return metas


class DemTile:
    """One GeoTIFF (first IFD). Sampling in degrees: height(lat, lon)."""

    def __init__(self, path):
        with open(path, "rb") as f:
            self.data = f.read()
        d = self.data
        if d[:2] == b"II":
            self.e = "<"
        elif d[:2] == b"MM":
            self.e = ">"
        else:
            raise DemError("not a TIFF: %s" % path)
        magic = struct.unpack(self.e + "H", d[2:4])[0]
        if magic == 42:
            self.big = False
            ifd = struct.unpack(self.e + "I", d[4:8])[0]
        elif magic == 43:
            self.big = True
            ifd = struct.unpack(self.e + "Q", d[8:16])[0]
        else:
            raise DemError("bad TIFF magic %d" % magic)
        self.tags = self._read_ifd(ifd)
        t = self.tags
        self.width = t[256][0]
        self.rows = t[257][0]
        if t.get(259, (1,))[0] not in (8, 32946):
            raise DemError("unsupported compression %r (only deflate)" % (t.get(259),))
        self.predictor = t.get(317, (1,))[0]
        if self.predictor not in (1, 3):
            raise DemError("unsupported predictor %d" % self.predictor)
        if t.get(339, (1,))[0] != 3 or t.get(258, (32,))[0] != 32:
            raise DemError("only float32 samples are supported")
        if t.get(277, (1,))[0] != 1:
            raise DemError("only one sample per pixel is supported")
        if 322 in t:
            self.tw, self.th = t[322][0], t[323][0]
            self.offsets, self.counts = t[324], t[325]
        else:  # striped: a strip is a tile as wide as the image
            self.tw, self.th = self.width, t.get(278, (self.rows,))[0]
            self.offsets, self.counts = t[273], t[279]
        self.tiles_x = (self.width + self.tw - 1) // self.tw
        scale = t[33550]
        tie = t[33922]
        self.dx, self.dy = scale[0], scale[1]
        area = True
        keys = t.get(34735)
        if keys:  # GeoKeyDirectory: GTRasterTypeGeoKey (1025) 1 = PixelIsArea, 2 = PixelIsPoint
            for i in range(4, len(keys) - 3, 4):
                if keys[i] == 1025 and keys[i + 1] == 0:
                    area = keys[i + 3] != 2
        self.pixel_is_area = area
        self.lon0 = tie[3] - tie[0] * self.dx  # lon of the left edge of column 0 (area) / of column 0 itself (point)
        self.lat0 = tie[4] + tie[1] * self.dy
        self._cache = {}

    def _read_ifd(self, off):
        d, e = self.data, self.e
        if self.big:
            n = struct.unpack(e + "Q", d[off:off + 8])[0]
            base, esz, cntfmt, offsz = off + 8, 20, "Q", 8
        else:
            n = struct.unpack(e + "H", d[off:off + 2])[0]
            base, esz, cntfmt, offsz = off + 2, 12, "I", 4
        tags = {}
        for i in range(n):
            ent = d[base + i * esz: base + (i + 1) * esz]
            tag, typ = struct.unpack(e + "HH", ent[:4])
            cnt = struct.unpack(e + cntfmt, ent[4:4 + offsz])[0]
            size = _TYPE_SIZE.get(typ)
            if size is None:
                continue
            raw = ent[4 + offsz:]
            if size * cnt > offsz:
                voff = struct.unpack(e + ("Q" if self.big else "I"), raw[:offsz])[0]
                raw = d[voff:voff + size * cnt]
            else:
                raw = raw[:size * cnt]
            if typ == 2:
                tags[tag] = (raw.decode("ascii", "replace").rstrip("\0"),)
            elif typ in (5, 10):
                fmt = "II" if typ == 5 else "ii"
                vals = struct.unpack(e + fmt[0] * (2 * cnt), raw)
                tags[tag] = tuple(vals[k] / vals[k + 1] for k in range(0, len(vals), 2))
            else:
                tags[tag] = struct.unpack(e + _TYPE_FMT[typ] * cnt, raw)
        return tags

    def _decode(self, idx):
        hit = self._cache.get(idx)
        if hit is not None:
            return hit
        raw = zlib.decompress(self.data[self.offsets[idx]: self.offsets[idx] + self.counts[idx]])
        w, h = self.tw, self.th
        if self.predictor == 3:
            out = array("f")
            rowlen = 4 * w
            if len(raw) < rowlen * h:
                raise DemError("tile %d too short" % idx)
            for r in range(h):
                row = raw[r * rowlen:(r + 1) * rowlen]
                row = bytes(a & 255 for a in accumulate(row))  # undo the byte-wise horizontal difference
                b = bytearray(rowlen)  # byte planes, most significant first -> little-endian float32
                b[3::4] = row[0:w]
                b[2::4] = row[w:2 * w]
                b[1::4] = row[2 * w:3 * w]
                b[0::4] = row[3 * w:4 * w]
                out.frombytes(bytes(b))
            if struct.pack("=H", 1) != struct.pack("<H", 1):
                out.byteswap()
        else:
            out = array("f")
            out.frombytes(raw[:4 * w * h])
            if self.e != ("<" if struct.pack("=H", 1) == struct.pack("<H", 1) else ">"):
                out.byteswap()
        if len(self._cache) >= 24:
            self._cache.pop(next(iter(self._cache)))
        self._cache[idx] = out
        return out

    def _px(self, col, row):
        col = min(max(col, 0), self.width - 1)
        row = min(max(row, 0), self.rows - 1)
        tile = (row // self.th) * self.tiles_x + (col // self.tw)
        v = self._decode(tile)[(row % self.th) * self.tw + (col % self.tw)]
        return None if (v != v or v < NODATA_BELOW) else v

    def contains(self, lat, lon):
        return (self.lon0 <= lon < self.lon0 + self.width * self.dx) and (self.lat0 - self.rows * self.dy < lat <= self.lat0)

    def height(self, lat, lon):
        """Bilinear height in metres, or None when all four neighbours are void."""
        fx = (lon - self.lon0) / self.dx - (0.5 if self.pixel_is_area else 0.0)
        fy = (self.lat0 - lat) / self.dy - (0.5 if self.pixel_is_area else 0.0)
        c0, r0 = int(math.floor(fx)), int(math.floor(fy))
        tx, ty = fx - c0, fy - r0
        vals = [(self._px(c0, r0), (1 - tx) * (1 - ty)), (self._px(c0 + 1, r0), tx * (1 - ty)),
                (self._px(c0, r0 + 1), (1 - tx) * ty), (self._px(c0 + 1, r0 + 1), tx * ty)]
        good = [(v, w) for v, w in vals if v is not None]
        if not good:
            return None
        wsum = sum(w for _, w in good)
        if wsum <= 0:
            return sum(v for v, _ in good) / len(good)
        return sum(v * w for v, w in good) / wsum


class DemSet:
    """Several tiles; counts the void samples (reported in the provenance)."""

    def __init__(self, paths):
        self.tiles = [DemTile(p) for p in paths]
        self.queries = 0
        self.voids = 0

    def height(self, lat, lon):
        """Height in metres, or 0.0 for a void (counted in .voids)."""
        self.queries += 1
        for t in self.tiles:
            if t.contains(lat, lon):
                h = t.height(lat, lon)
                if h is not None:
                    return h
                break
        self.voids += 1
        return 0.0


def grade_series(s, y, half_m=30.0):
    """Grade in percent per sample from distance s[] and height y[] (same length): central difference over +-half_m, one-sided at the ends."""
    n = len(s)
    out = []
    lo = hi = 0
    for i in range(n):
        while s[i] - s[lo] > half_m:
            lo += 1
        hi = max(hi, i)
        while hi + 1 < n and s[hi + 1] - s[i] <= half_m:
            hi += 1
        # at least one neighbour on each available side, even when the sampling is coarser than half_m
        a = lo if lo < i or i == 0 else i - 1
        b = hi if hi > i or i == n - 1 else i + 1
        ds = s[b] - s[a]
        out.append(0.0 if ds <= 0 else 100.0 * (y[b] - y[a]) / ds)
    return out


if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser(description="Print the DEM height at lat lon from a GLO-30 tile")
    ap.add_argument("tif")
    ap.add_argument("lat", type=float)
    ap.add_argument("lon", type=float)
    a = ap.parse_args()
    t = DemTile(a.tif)
    print("%dx%d tile %dx%d dx=%g dy=%g area=%s height=%s" % (t.width, t.rows, t.tw, t.th, t.dx, t.dy, t.pixel_is_area, t.height(a.lat, a.lon)))
