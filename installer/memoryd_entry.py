"""PyInstaller entry point for the frozen memoryd sidecar (spec 011 T001).

PyInstaller analyzes a *script*, not a ``-m`` module, and ``lore_memoryd.__main__``
uses package-relative imports that only resolve when imported as part of the
package. This one-line wrapper imports the package's ``main`` and runs it, so the
frozen exe behaves exactly like ``python -m lore_memoryd`` (including ``--selftest``).
"""

from lore_memoryd.__main__ import main

if __name__ == "__main__":
    main()
