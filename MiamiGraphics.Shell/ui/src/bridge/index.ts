import type { IAppBridge } from './IAppBridge';
import { WebViewBridge } from './webviewBridge';
import { MockBridge } from './mockBridge';

export type * from './types';
export type * from './IAppBridge';

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (msg: string) => void;
        addEventListener: (...args: unknown[]) => void;
      };
    };
  }
}

export const bridge: IAppBridge = window.chrome?.webview
  ? new WebViewBridge()
  : new MockBridge();

(window as unknown as { bridge: IAppBridge }).bridge = bridge;
