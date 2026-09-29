import { RouterProvider } from 'react-router-dom';
import { router } from './router';
import { ThemeProvider } from './shared/theme/ThemeContext';
import { ToastProvider } from './shared/ds/Toast';
import { Spinner } from './shared/ds';
import { useAuthBootstrap } from './core/auth/useAuthBootstrap';

export function App() {
  const { ready } = useAuthBootstrap();

  return (
    <ThemeProvider>
      <ToastProvider>{!ready ? <Spinner fullscreen /> : <RouterProvider router={router} />}</ToastProvider>
    </ThemeProvider>
  );
}
