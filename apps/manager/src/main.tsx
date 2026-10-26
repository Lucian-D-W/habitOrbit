import { createRoot } from 'react-dom/client';
import './style.css';
import {App} from './App';
import {FontProvider} from './fonts';
createRoot(document.getElementById('root')!).render(
  <FontProvider><App/></FontProvider>
);
