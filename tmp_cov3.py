import xml.etree.ElementTree as ET

root = ET.parse("Agentic.Chat.Tests/coverage/coverage.cobertura.xml").getroot()
print("packages:")
for pkg in root.iter("package"):
    print(f"  {pkg.get('name')} line={pkg.get('line-rate')} branch={pkg.get('branch-rate')}")
print("\nall classes under 100%:")
for cls in root.iter("class"):
    rate = float(cls.get("line-rate", 0))
    br = float(cls.get("branch-rate", 0))
    if rate < 1.0 or br < 1.0:
        print(f"  L={rate*100:.1f}% B={br*100:.1f}% {cls.get('name')}")
        for line in cls.iter("line"):
            hits = line.get("hits")
            branch = line.get("branch")
            cond = line.get("condition-coverage", "")
            if hits == "0" or (branch == "True" and cond and not cond.startswith("100%")):
                print(f"    L{line.get('number')} hits={hits} {cond}")
