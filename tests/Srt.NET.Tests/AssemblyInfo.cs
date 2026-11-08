// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

// Copyright (c) 2026 Noah Davis

using Xunit.Sdk;
using Xunit.v3;

// libsrt logging and runtime startup are process-wide, so tests run one at a time.
[assembly: Parallelization(Mode = ParallelMode.None)]
