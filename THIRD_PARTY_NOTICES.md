# Third-party notices

Every dependency is listed here with its pinned version and license (PRD 39). Upgrades happen only in
a dedicated dependency-update commit and must update this file and `Scripts/verify-dependencies.sh`.

## Runtime dependencies

| Component | Version | License | Source |
| --- | --- | --- | --- |
| Unity Engine | 6000.3.20f1 (Unity 6.3 LTS) | Unity Software License / Unity Terms of Service | https://unity.com |
| UniGLTF (`com.vrmc.gltf`) | v0.131.2 | MIT | https://github.com/vrm-c/UniVRM |
| UniVRM 0.x (`com.vrmc.univrm`) | v0.131.2 | MIT | https://github.com/vrm-c/UniVRM |
| UniVRM 1.0 (`com.vrmc.vrm`) | v0.131.2 | MIT | https://github.com/vrm-c/UniVRM |
| Unity Test Framework (`com.unity.test-framework`) | 1.5.1 | Unity Companion License | Unity Registry |
| Unity UI (`com.unity.ugui`, required by UniVRM) | 2.0.0 | Unity Companion License | Unity Registry |
| Unity Timeline (`com.unity.timeline`, pulled in by `com.vrmc.vrm`) | resolved by UPM | Unity Companion License | Unity Registry |
| Unity Mathematics (`com.unity.mathematics`, pulled in by `com.vrmc.gltf`) | resolved by UPM | Unity Companion License | Unity Registry |

| Noto Sans TC (`Assets/VRMCast/UI/Fonts/NotoSansTC-Regular.otf`) | Noto CJK 2.004 static TC subset | SIL Open Font License 1.1 (`Assets/VRMCast/UI/Fonts/LICENSE-NotoSansTC.txt`) | https://github.com/notofonts/noto-cjk |

UniVRM bundles MToon (MIT, Santarh / VRM Consortium) and UniHumanoid (MIT).

## Development-only dependencies

| Component | Version | License | Use |
| --- | --- | --- | --- |
| NUnit | 3.14.0 (via Unity Test Framework; also restored by `Scripts/run-core-tests.sh`) | MIT | tests |
| NUnit3TestAdapter | 4.6.0 | MIT | `Scripts/run-core-tests.sh` only |
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT | `Scripts/run-core-tests.sh` only |
| .NET SDK | 8.0 | MIT | `Scripts/run-core-tests.sh` only |
| JetBrains Rider / Visual Studio editor packages | 3.0.36 / 2.0.23 | Unity Companion License | IDE integration |

## Planned (not yet in the project)

| Component | License | Milestone |
| --- | --- | --- |
| MediaPipeUnityPlugin (homuler) | MIT; bundles Google MediaPipe (Apache-2.0) and its models | MVP-B |
| Apple Core Media I/O Camera Extension APIs | Apple SDK license | MVP-E |

No paid assets, no cloud inference, no network dependency for runtime tracking (PRD 40, 41).

## License texts

### UniVRM (MIT)

```
MIT License

Copyright (c) 2020 VRM Consortium
Copyright (c) 2018 Masataka SUMI for MToon

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
