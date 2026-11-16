from __future__ import annotations
import argparse, collections, gzip, hashlib, json, mimetypes, re, stat, tarfile, zipfile
from pathlib import Path, PurePosixPath

OK, INVALID, ERROR, INCOMPLETE = 0, 1, 2, 3
IMAGES={".png",".jpg",".jpeg",".tif",".tiff",".webp",".exr",".hdr"}
MODELS={".obj",".gltf",".glb",".fbx",".blend",".dae",".stl",".ply"}
ARCHIVES={".zip",".tar",".tgz",".gz",".7z"}
MAX_FILES=100000
MAX_MEMBER=4*1024**3
MAX_TOTAL=20*1024**3
MAX_RATIO=10000

def sha256(path):
    h=hashlib.sha256()
    with Path(path).open("rb") as f:
        for chunk in iter(lambda:f.read(1024*1024),b""): h.update(chunk)
    return h.hexdigest()

def safe_member(name):
    n=name.replace("\\\\","/")
    p=PurePosixPath(n)
    return not (p.is_absolute() or re.match(r"^[A-Za-z]:",n) or ".." in p.parts)

def archive_report(path):
    out={"path":str(path),"members":[],"errors":[],"warnings":[]}
    total=0
    def add(name,size,compressed=None):
        nonlocal total
        if not safe_member(name): out["errors"].append({"code":"ARCHIVE_PATH_TRAVERSAL","member":name})
        if size<0 or size>MAX_MEMBER: out["errors"].append({"code":"ARCHIVE_MEMBER_SIZE_LIMIT","member":name,"size":size})
        total+=max(0,size)
        if compressed and compressed>0 and size/compressed>MAX_RATIO: out["errors"].append({"code":"ARCHIVE_RATIO_LIMIT","member":name})
        out["members"].append({"name":name,"size_bytes":size})
        if len(out["members"])>MAX_FILES: out["errors"].append({"code":"ARCHIVE_FILE_COUNT_LIMIT"})
    try:
        if zipfile.is_zipfile(path):
            with zipfile.ZipFile(path) as z:
                bad=z.testzip()
                if bad: out["errors"].append({"code":"ARCHIVE_CRC_FAILURE","member":bad})
                for i in z.infolist():
                    if stat.S_ISLNK(i.external_attr>>16): out["errors"].append({"code":"ARCHIVE_SYMLINK","member":i.filename})
                    add(i.filename,i.file_size,i.compress_size)
        elif tarfile.is_tarfile(path):
            with tarfile.open(path,"r:*") as t:
                for i in t.getmembers():
                    if i.issym() or i.islnk() or i.isdev(): out["errors"].append({"code":"ARCHIVE_LINK_OR_DEVICE","member":i.name})
                    add(i.name,i.size)
        elif Path(path).suffix.lower()==".gz":
            with gzip.open(path,"rb") as f:
                while f.read(1024*1024):
                    total+=1024*1024
                    if total>MAX_MEMBER: raise ValueError("gzip expansion limit exceeded")
        elif Path(path).suffix.lower()==".7z":
            try: import py7zr
            except ImportError: out["incomplete"]="Install optional py7zr to inspect 7z archives."
            else:
                with py7zr.SevenZipFile(path,"r") as z:
                    for i in z.list(): add(i.filename,i.uncompressed or 0,i.compressed)
        else: out["errors"].append({"code":"UNSUPPORTED_OR_INVALID_ARCHIVE"})
    except Exception as e: out["errors"].append({"code":"ARCHIVE_READ_ERROR","message":str(e)})
    if total>MAX_TOTAL: out["errors"].append({"code":"ARCHIVE_TOTAL_SIZE_LIMIT","size":total})
    out["expanded_size_bytes"]=total
    out["member_count"]=len(out["members"])
    out["exit_code"]=INVALID if out["errors"] else INCOMPLETE if out.get("incomplete") else OK
    return out

def image_report(path):
    try: from PIL import Image
    except ImportError: return {"inspection":"unavailable","incomplete":"Install optional Pillow."}
    try:
        with Image.open(path) as im:
            return {"format":im.format,"width":im.width,"height":im.height,
                    "aspect_ratio":round(im.width/im.height,6) if im.height else None,
                    "mode":im.mode,"has_alpha":"A" in im.getbands(),
                    "bits_per_channel_estimate":16 if "16" in im.mode else 8,
                    "icc_profile_present":bool(im.info.get("icc_profile")),
                    "color_metadata":{k:im.info[k] for k in ("srgb","gamma") if k in im.info}}
    except Exception as e: return {"error":str(e)}

def model_report(path):
    ext=Path(path).suffix.lower()
    out={"format":ext.lstrip(".").upper(),"inspection":"metadata_only","size_bytes":Path(path).stat().st_size}
    if ext==".obj":
        v=uv=vn=tri=0; mtl=set()
        try:
            with open(path,encoding="utf-8",errors="replace") as f:
                for line in f:
                    s=line.strip()
                    if s.startswith("v "): v+=1
                    elif s.startswith("vt "): uv+=1
                    elif s.startswith("vn "): vn+=1
                    elif s.startswith("f "): tri+=max(0,len(s.split())-3)
                    elif s.startswith("mtllib "): mtl.add(s[7:].strip())
            out.update(inspection="parsed_obj",vertices=v,triangles=tri,uv_coordinates=uv,normals=vn,has_uv=uv>0,external_dependencies=sorted(mtl))
        except OSError as e: out["error"]=str(e)
    elif ext in (".gltf",".glb"):
        try:
            if ext==".gltf": doc=json.loads(Path(path).read_text(encoding="utf-8"))
            else:
                import struct
                with open(path,"rb") as f: head=f.read(20); magic,ver,length,chunk,kind=struct.unpack("<4sIIII",head)
                if magic!=b"glTF" or ver!=2 or kind!=0x4E4F534A or chunk>64*1024*1024: raise ValueError("Invalid/unsupported GLB JSON chunk")
                with open(path,"rb") as f: f.seek(20); doc=json.loads(f.read(chunk).decode("utf-8"))
            meshes=doc.get("meshes",[])
            out.update(inspection="parsed_gltf_metadata",mesh_count=len(meshes),
              primitive_count=sum(len(m.get("primitives",[])) for m in meshes),
              material_count=len(doc.get("materials",[])),texture_count=len(doc.get("textures",[])),
              has_uv=any("TEXCOORD_0" in p.get("attributes",{}) for m in meshes for p in m.get("primitives",[])),
              external_dependencies=[x["uri"] for x in doc.get("buffers",[])+doc.get("images",[]) if x.get("uri") and not x["uri"].startswith("data:")])
        except Exception as e: out["error"]=str(e)
    else: out["incomplete"]="Deep inspection not implemented for this model format."
    return out

def inspect(path):
    path=Path(path)
    if not path.exists(): return {"errors":[{"code":"MISSING_PATH","path":str(path)}],"exit_code":INVALID}
    files=[path] if path.is_file() else sorted(p for p in path.rglob("*") if p.is_file())
    rows=[]; errors=[]
    for p in files:
        try:
            row={"path":str(p.relative_to(path if path.is_dir() else path.parent)).replace("\\\\","/"),
                 "size_bytes":p.stat().st_size,"sha256":sha256(p),"extension":p.suffix.lower(),
                 "mime_guess":mimetypes.guess_type(p.name)[0]}
            ext=p.suffix.lower()
            if ext in IMAGES: row["image"]=image_report(p)
            if ext in MODELS: row["model"]=model_report(p)
            if ext in ARCHIVES:
                row["archive"]=archive_report(p); errors.extend(row["archive"]["errors"])
            for kind in ("image","model"):
                if row.get(kind,{}).get("error"): errors.append({"code":kind.upper()+"_INSPECTION_ERROR","path":str(p)})
            rows.append(row)
        except OSError as e: errors.append({"code":"FILE_READ_ERROR","path":str(p),"message":str(e)})
    return {"root":str(path),"files":rows,"errors":errors,"exit_code":INVALID if errors else OK}

def validate_registry(registry,schema):
    errors=[]; incomplete=[]
    try: doc=json.loads(Path(registry).read_text(encoding="utf-8")); sch=json.loads(Path(schema).read_text(encoding="utf-8"))
    except Exception as e: return {"status":"error","errors":[{"code":"REGISTRY_READ_ERROR","message":str(e)}],"exit_code":ERROR}
    try: import jsonschema
    except ImportError: return {"status":"incomplete","incomplete":[{"code":"JSONSCHEMA_MISSING","message":"Install jsonschema dependency."}],"exit_code":INCOMPLETE}
    validator=jsonschema.Draft202012Validator(sch,format_checker=jsonschema.FormatChecker())
    for e in sorted(validator.iter_errors(doc),key=lambda x:list(map(str,x.path))):
        errors.append({"code":"SCHEMA_VALIDATION","path":"/".join(map(str,e.path)),"message":e.message})
    ids=set()
    for n,a in enumerate(doc.get("assets",[])):
        aid=a.get("asset_id")
        if aid in ids: errors.append({"code":"DUPLICATE_ASSET_ID","asset_id":aid})
        ids.add(aid)
        if a.get("status")!="approved": errors.append({"code":"NON_APPROVED_IN_ACCEPTED_REGISTRY","index":n})
        lic=a.get("license",{})
        if lic.get("spdx_id")!="CC0-1.0" or lic.get("verified") is not True: errors.append({"code":"LICENSE_NOT_APPROVED","index":n})
        for j,f in enumerate(a.get("files",[])):
            if not f.get("download_url"): incomplete.append({"code":"FILE_DOWNLOAD_EVIDENCE_MISSING","index":n,"file_index":j})
    code=INVALID if errors else INCOMPLETE if incomplete else OK
    return {"status":"invalid" if errors else "incomplete" if incomplete else "valid",
            "asset_count":len(doc.get("assets",[])),"errors":errors,"incomplete":incomplete,"exit_code":code}

def duplicates(root):
    hashes=collections.defaultdict(list); names=collections.defaultdict(list)
    for p in sorted(x for x in Path(root).rglob("*") if x.is_file()):
        try: hashes[sha256(p)].append(str(p.relative_to(root))); names[p.name.casefold()].append(str(p.relative_to(root)))
        except OSError: pass
    return {"exact_duplicates":[{"sha256":h,"paths":v} for h,v in sorted(hashes.items()) if len(v)>1],
            "filename_collisions":[{"filename":n,"paths":v} for n,v in sorted(names.items()) if len(v)>1],
            "note":"Advisory only; no automatic deletion."}

def package_report(root):
    root=Path(root); out=inspect(root); manifest=root/"package-manifest.json"
    if not manifest.exists():
        out["errors"].append({"code":"PACKAGE_MANIFEST_MISSING"}); out["exit_code"]=INCOMPLETE; return out
    try:
        doc=json.loads(manifest.read_text(encoding="utf-8")); expected=set()
        for entry in doc.get("files",[]):
            rel=entry["path"]
            if not safe_member(rel): out["errors"].append({"code":"MANIFEST_PATH_INVALID","path":rel}); continue
            expected.add(rel); p=root/rel
            if not p.is_file(): out["errors"].append({"code":"PACKAGE_FILE_MISSING","path":rel})
            elif sha256(p).lower()!=entry.get("sha256","").lower(): out["errors"].append({"code":"CHECKSUM_MISMATCH","path":rel})
        actual={str(p.relative_to(root)).replace("\\\\","/") for p in root.rglob("*") if p.is_file() and p!=manifest}
        for rel in sorted(actual-expected): out["errors"].append({"code":"UNEXPECTED_FILE","path":rel})
    except Exception as e: out["errors"].append({"code":"PACKAGE_MANIFEST_INVALID","message":str(e)})
    out["exit_code"]=INVALID if out["errors"] else OK
    return out

def emit(data,output=None):
    text=json.dumps(data,ensure_ascii=False,indent=2,sort_keys=True)+"\\n"
    if output: Path(output).write_text(text,encoding="utf-8")
    else: print(text,end="")
    return data.get("exit_code",OK)

def main(argv=None):
    p=argparse.ArgumentParser(prog="assetlib"); s=p.add_subparsers(dest="cmd",required=True)
    q=s.add_parser("validate-registry"); q.add_argument("--registry",default="asset-library/registry/assets.json"); q.add_argument("--schema",default="asset-library/schemas/asset.schema.json"); q.add_argument("--output")
    for name in ("inspect","hash","scan-duplicates","inspect-archive","report","validate-package"):
        q=s.add_parser(name); q.add_argument("path"); q.add_argument("--output")
    a=p.parse_args(argv)
    try:
        if a.cmd=="validate-registry": data=validate_registry(a.registry,a.schema)
        elif a.cmd=="scan-duplicates": data=duplicates(a.path); data["exit_code"]=OK
        elif a.cmd=="inspect-archive": data=archive_report(a.path)
        elif a.cmd=="validate-package": data=package_report(a.path)
        elif a.cmd=="hash":
            root=Path(a.path); fs=[root] if root.is_file() else sorted(x for x in root.rglob("*") if x.is_file())
            data={"files":[{"path":str(x),"size_bytes":x.stat().st_size,"sha256":sha256(x)} for x in fs],"exit_code":OK}
        else:
            data=inspect(a.path)
            if a.cmd=="report": data["duplicates"]=duplicates(a.path)
        return emit(data,a.output)
    except Exception as e:
        print(json.dumps({"status":"error","message":str(e)})); return ERROR

if __name__=="__main__": raise SystemExit(main())
