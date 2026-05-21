import os from 'os';

type AppsState = {
    platform: string;
    isWindows: boolean;
    isLinux: boolean;
    sleuthkitInstalled: boolean;
};

const state : AppsState = {
    platform: '',
    isWindows: false,
    isLinux: false,
    sleuthkitInstalled: false,
}

export async function initializeAppState() {
    state.platform = os.platform();
    state.isWindows = state.platform === 'win32';
    state.isLinux = state.platform === 'linux';

    // state.sleuthkitInstalled = await checkSleuthkit();

    console.log('App State Initialized:', state);
}

export function getAppState() {
    return state;
}